using System.IO;
using KVD.Utils;
using KVD.Utils.DataStructures;
using Unity.Content;
using UnityEngine;
using UnityEngine.PlayerLoop;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace KVD.Prometheus
{
	public partial class PrometheusLoader
	{
		public static string PrometheusPath => Path.Combine(Application.streamingAssetsPath, "Prometheus");
		public static string PrometheusArchivesPath => Path.Combine(PrometheusPath, "Archives");
		public static string PrometheusMetaPath => Path.Combine(PrometheusPath, "Meta");
		public static string PrometheusDataPath => Path.Combine(PrometheusMetaPath, "PrometheusData.bin");

		PrometheusMapping _prometheusMapping;

		ContentNamespace _contentNamespace;

		PrometheusLoader()
		{
			PlayerLoopUtils.RegisterToPlayerLoopBegin<PrometheusLoader, PreUpdate>(Update);

			_contentNamespace = ContentNamespace.GetOrCreateNamespace("Prometheus");
			_prometheusMapping = LoadContentFilesData();

			InitFileManagement();
			InitCallbacks();

			EditorInitCallbacks();
		}

		public Option<T> GetAsset<T>(PrometheusIdentifier prometheusIdentifier) where T : Object
		{
			if (!_prometheusMapping.Asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var hasAsset = false;
				var result = Option<T>.None;
				EditorGetAsset(prometheusIdentifier, ref hasAsset, ref result);
				if (hasAsset)
				{
					return result;
				}
				else
				{
					Debug.LogError($"Asset {prometheusIdentifier.assetGuid}[{prometheusIdentifier.localIdentifier}] not found in Prometheus");
					return Option<T>.Some(null);
				}
			}

			var loadingIndex = _contentFile2Index[contentFileGuid];
			ref var load = ref _contentFileLoads[loadingIndex];
			return IsSuccessfullyLoaded(load) ?
				Option<T>.Some((T)load.contentFile.GetObject(_prometheusMapping.Asset2LocalIdentifier[prometheusIdentifier])) :
				Option<T>.None;
		}

		public Option<T> ForceGetAsset<T>(PrometheusIdentifier prometheusIdentifier) where T : Object
		{
			if (!_prometheusMapping.Asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var hasAsset = false;
				var result = Option<T>.None;
				EditorForceGetAsset(prometheusIdentifier, ref hasAsset, ref result);
				if (hasAsset)
				{
					return result;
				}
				else
				{
					Debug.LogError($"Asset {prometheusIdentifier.assetGuid}[{prometheusIdentifier.localIdentifier}] not found in Prometheus");
					return Option<T>.None;
				}
			}

			if (!_contentFile2Index.TryGetValue(contentFileGuid, out var loadingIndex))
			{
				Debug.LogError($"Asset {prometheusIdentifier.assetGuid}[{prometheusIdentifier.localIdentifier}] not started loading");
				return Option<T>.None;
			}
			ref var load = ref _contentFileLoads[loadingIndex];

			if (!IsLoaded(load))
			{
				ForceLoad(ref load);
			}

			return IsSuccessfullyLoaded(load) ?
				Option<T>.Some((T)load.contentFile.GetObject(_prometheusMapping.Asset2LocalIdentifier[prometheusIdentifier])) :
				Option<T>.None;
		}

		public bool IsActive(in PrometheusIdentifier prometheusIdentifier)
		{
			if (!_prometheusMapping.Asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var isEditorActive = false;
				EditorIsActive(prometheusIdentifier, ref isEditorActive);
				return isEditorActive;
			}

			if (!_contentFile2Index.TryGetValue(contentFileGuid, out _))
			{
				return false;
			}

			return true;
		}

		public bool IsLoading(in PrometheusIdentifier prometheusIdentifier)
		{
			if (!_prometheusMapping.Asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var isEditorLoading = false;
				EditorIsLoading(prometheusIdentifier, ref isEditorLoading);
				return isEditorLoading;
			}

			if (!_contentFile2Index.TryGetValue(contentFileGuid, out var loadingIndex))
			{
				return false;
			}

			return !IsLoaded(_contentFileLoads[loadingIndex]);
		}

		public bool IsLoaded(in PrometheusIdentifier prometheusIdentifier)
		{
			if (!_prometheusMapping.Asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var isLoaded = false;
				EditorIsLoaded(prometheusIdentifier, ref isLoaded);
				return isLoaded;
			}

			if (!_contentFile2Index.TryGetValue(contentFileGuid, out var loadingIndex))
			{
				return false;
			}

			return IsLoaded(_contentFileLoads[loadingIndex]);
		}

		public bool IsSuccessfullyLoaded(in PrometheusIdentifier prometheusIdentifier)
		{
			if (!_prometheusMapping.Asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var isLoaded = false;
				EditorIsLoaded(prometheusIdentifier, ref isLoaded);
				return isLoaded;
			}

			if (!_contentFile2Index.TryGetValue(contentFileGuid, out var loadingIndex))
			{
				return false;
			}

			return IsSuccessfullyLoaded(_contentFileLoads[loadingIndex]);
		}

		public void StartAssetLoading(PrometheusIdentifier prometheusIdentifier)
		{
			if (!_prometheusMapping.Asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var availableInEditor = false;
				EditorIsAssetAvailable(prometheusIdentifier, ref availableInEditor);
				if (!availableInEditor)
				{
					Debug.LogError($"Asset {prometheusIdentifier.assetGuid}[{prometheusIdentifier.localIdentifier}] not found in Prometheus");
				}
				return;
			}

			StartLoading(contentFileGuid);
		}

		public void StartAssetUnloading(PrometheusIdentifier prometheusIdentifier)
		{
			if (!_prometheusMapping.Asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var availableInEditor = false;
				EditorIsAssetAvailable(prometheusIdentifier, ref availableInEditor);
				if (!availableInEditor)
				{
					Debug.LogError($"Asset {prometheusIdentifier.assetGuid}[{prometheusIdentifier.localIdentifier}] not found in Prometheus");
				}
				return;
			}

			StartUnloading(contentFileGuid);
		}

		public bool CanLoadAsset(PrometheusIdentifier prometheusIdentifier)
		{
			if (!_prometheusMapping.Asset2ContentFile.ContainsKey(prometheusIdentifier))
			{
				var availableInEditor = false;
				EditorIsAssetAvailable(prometheusIdentifier, ref availableInEditor);
				if (!availableInEditor)
				{
					Debug.LogError($"Asset {prometheusIdentifier.assetGuid}[{prometheusIdentifier.localIdentifier}] not found in Prometheus");
				}
				return availableInEditor;
			}
			return true;
		}

		void Update()
		{
			UpdateFileManagement();
			UpdateCallbacks();

			EditorUpdateCallbacks();
		}

		static PrometheusMapping LoadContentFilesData()
		{
			var path = PrometheusDataPath;
			PrometheusMapping prometheusData;
			if (File.Exists(path))
			{
				prometheusData = new();
				prometheusData.Deserialize(path);
			}
			else
			{
				Debug.LogError("ContentFilesData not found");
				prometheusData = PrometheusMapping.Fresh();
			}
			return prometheusData;
		}
	}

	static class PrometheusStateExt
	{
		public static bool IsLoading(this PrometheusLoader.State state)
		{
			return state is PrometheusLoader.State.WaitingForMounting or
				PrometheusLoader.State.Mounting or
				PrometheusLoader.State.WaitingForDependencies or
				PrometheusLoader.State.Loading;
		}

		public static bool IsUnloading(this PrometheusLoader.State state)
		{
			return state is PrometheusLoader.State.WaitingToStartUnloading or
				PrometheusLoader.State.Unloading or
				PrometheusLoader.State.WaitingForUnmount or
				PrometheusLoader.State.Unmounting;
		}

		public static bool IsLoaded(this PrometheusLoader.State state)
		{
			return state is PrometheusLoader.State.Loaded or PrometheusLoader.State.ErrorArchive or PrometheusLoader.State.ErrorContentFiles;
		}
	}
}
