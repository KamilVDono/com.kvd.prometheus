using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KVD.Utils.DataStructures;
using KVD.Utils.Editor;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Tasks;
using UnityEditor.Build.Pipeline.Utilities;
using UnityEditor.Build.Utilities;
using UnityEngine;
using BuildCompression = UnityEngine.BuildCompression;

namespace KVD.Prometheus.Editor
{
	public static class PrometheusCreator
	{
		[MenuItem(KVDConsts.MenuItemPrefix+"/Prometheus/Build Prometheus", false, 10)]
		public static void BuildPrometheus()
		{
			var baseDirectoryPath = PrometheusPersistence.BaseDirectoryPath;
			if (!Directory.Exists(baseDirectoryPath))
			{
				Directory.CreateDirectory(baseDirectoryPath);
			}

			var archivesDirectoryPath = PrometheusPersistence.ArchivesDirectoryPath;

			if (Directory.Exists(archivesDirectoryPath))
			{
				foreach (var file in Directory.EnumerateFiles(archivesDirectoryPath).ToArray())
				{
					File.Delete(file);
				}
			}
			else
			{
				Directory.CreateDirectory(archivesDirectoryPath);
			}

			var mappingPath = PrometheusPersistence.MappingsFilePath;
			if (File.Exists(mappingPath))
			{
				File.Delete(mappingPath);
			}

			var buildTarget = EditorUserBuildSettings.activeBuildTarget;
			var buildGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
			var success = BuildPrometheus(buildTarget, buildGroup);
			if (success)
			{
				Debug.Log($"Prometheus built to {baseDirectoryPath}");
				AssetDatabase.Refresh();
			}
			else
			{
				Debug.LogError("Failed to build Prometheus");
			}
		}

		static bool BuildPrometheus(BuildTarget buildTarget, BuildTargetGroup buildGroup)
		{
			var settings = PrometheusSettings.Instance;

			var bundleBuilds = new AssetBundleBuild[1];

			var validAssetPaths = new HashSet<string>();
			foreach (var asset in PrometheusAssets.Instance.assets)
			{
				if (asset == null)
				{
					continue;
				}
				var path = AssetDatabase.GetAssetPath(asset);
				validAssetPaths.Add(path);
			}

			bundleBuilds[0] = new()
			{
				assetBundleName = "content",
				assetNames = validAssetPaths.ToArray(),
			};

			var buildContent = new BundleBuildContent(bundleBuilds);
			var buildParams = new BundleBuildParameters(buildTarget, buildGroup, PrometheusPersistence.ArchivesDirectoryPath);
			buildParams.BundleCompression = settings.compressionType switch
			{
				PrometheusSettings.CompressionType.Uncompressed => BuildCompression.Uncompressed,
				PrometheusSettings.CompressionType.LZ4          => BuildCompression.LZ4,
				PrometheusSettings.CompressionType.LZMA         => BuildCompression.LZMA,
				_                                               => throw new ArgumentOutOfRangeException(nameof(settings.compressionType), settings.compressionType, null)
			};
			buildParams.NonRecursiveDependencies = false;

			var tasks = DefaultBuildTasks.ContentFileCompatible();
			var buildLayout = new ClusterOutput();
			var contentIdentifiers = new ContentFileIdentifiers();
			var exitCode = ContentPipeline.BuildAssetBundles(buildParams, buildContent, out var result, tasks, contentIdentifiers, buildLayout);
			if (exitCode == ReturnCode.Success)
			{
				var contentFilesData = PrometheusMapping.Fresh();

				foreach (var (contentFileGuidStr, b) in result.WriteResults)
				{
					var contentFileGuid = new SerializableGuid(Guid.Parse(contentFileGuidStr));
					var dependencies = b.externalFileReferences.Select(x =>
						{
							if (x.filePath.Equals(CommonStrings.UnityDefaultResourcePath, StringComparison.OrdinalIgnoreCase))
							{
								return default;
							}
							return new SerializableGuid(x.filePath);
						})
						.ToArray();
					contentFilesData.ContentFile2Dependencies.Add(contentFileGuid, dependencies);

					foreach (var dependency in dependencies)
					{
						if (dependency == default)
						{
							continue;
						}
						if (!contentFilesData.ContentFile2Dependants.TryGetValue(dependency, out var dependants))
						{
							dependants = Array.Empty<SerializableGuid>();
						}
						Array.Resize(ref dependants, dependants.Length+1);
						dependants[^1] = contentFileGuid;
						contentFilesData.ContentFile2Dependants[dependency] = dependants;
					}
				}

				foreach (var (objectId, cluster) in buildLayout.ObjectToCluster)
				{
					var contentFile = new SerializableGuid(Guid.Parse(cluster.ToString()));

					var assetGuid = new SerializableGuid(objectId.guid);
					var assetIdentifier = new PrometheusIdentifier(assetGuid, objectId.localIdentifierInFile);

					contentFilesData.Asset2ContentFile.Add(assetIdentifier, contentFile);
					contentFilesData.Asset2LocalIdentifier.Add(assetIdentifier, unchecked((ulong)buildLayout.ObjectToLocalID[objectId]));
				}

				contentFilesData.Serialize(PrometheusPersistence.MappingsFilePath);

				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();

				EditorUtility.RequestScriptReload();
			}
			return exitCode == ReturnCode.Success;
		}
	}
}
