using System.IO;
using KVD.Utils;
using KVD.Utils.DataStructures;
using Unity.Collections;
using Unity.Content;
using Unity.Mathematics;
using UnityEngine.PlayerLoop;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace KVD.Prometheus
{
	public partial class PrometheusLoader
	{
		PrometheusMapping _prometheusMapping;

		ContentNamespace _contentNamespace;

		PrometheusLoader()
		{
			PlayerLoopUtils.RegisterToPlayerLoopBegin<PrometheusLoader, PreUpdate>(Update);

			_contentNamespace = ContentNamespace.GetOrCreateNamespace("Prometheus");
			_prometheusMapping = LoadContentFilesData();

			InitFilesManagement();
			InitCallbacks();

			EditorInitCallbacks();
		}

		public Option<T> GetAsset<T>(PrometheusIdentifier prometheusIdentifier) where T : Object
		{
			if (!_prometheusMapping.asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
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

			var loadingIndex = _unmanaged._contentFile2Index[contentFileGuid];
			ref var load = ref _unmanaged._contentFileLoads[loadingIndex];
			return IsSuccessfullyLoaded(load) ?
				Option<T>.Some((T)load.contentFile.GetObject(_prometheusMapping.asset2LocalIdentifier[prometheusIdentifier])) :
				Option<T>.None;
		}

		public Option<T> ForceGetAsset<T>(PrometheusIdentifier prometheusIdentifier) where T : Object
		{
			if (!_prometheusMapping.asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
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

			if (!_unmanaged._contentFile2Index.TryGetValue(contentFileGuid, out var loadingIndex))
			{
				Debug.LogError($"Asset {prometheusIdentifier.assetGuid}[{prometheusIdentifier.localIdentifier}] not started loading");
				return Option<T>.None;
			}
			ref var load = ref _unmanaged._contentFileLoads[loadingIndex];

			if (!IsLoaded(load))
			{
				ForceLoad(ref load);
			}

			return IsSuccessfullyLoaded(load) ?
				Option<T>.Some((T)load.contentFile.GetObject(_prometheusMapping.asset2LocalIdentifier[prometheusIdentifier])) :
				Option<T>.None;
		}

		public bool IsActive(in PrometheusIdentifier prometheusIdentifier)
		{
			if (!_prometheusMapping.asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var isEditorActive = false;
				EditorIsActive(prometheusIdentifier, ref isEditorActive);
				return isEditorActive;
			}

			if (!_unmanaged._contentFile2Index.TryGetValue(contentFileGuid, out _))
			{
				return false;
			}

			return true;
		}

		public bool IsLoading(in PrometheusIdentifier prometheusIdentifier)
		{
			if (!_prometheusMapping.asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var isEditorLoading = false;
				EditorIsLoading(prometheusIdentifier, ref isEditorLoading);
				return isEditorLoading;
			}

			if (!_unmanaged._contentFile2Index.TryGetValue(contentFileGuid, out var loadingIndex))
			{
				return false;
			}

			return !IsLoaded(_unmanaged._contentFileLoads[loadingIndex]);
		}

		public bool IsLoaded(in PrometheusIdentifier prometheusIdentifier)
		{
			if (!_prometheusMapping.asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var isLoaded = false;
				EditorIsLoaded(prometheusIdentifier, ref isLoaded);
				return isLoaded;
			}

			if (!_unmanaged._contentFile2Index.TryGetValue(contentFileGuid, out var loadingIndex))
			{
				return false;
			}

			return IsLoaded(_unmanaged._contentFileLoads[loadingIndex]);
		}

		public bool IsSuccessfullyLoaded(in PrometheusIdentifier prometheusIdentifier)
		{
			if (!_prometheusMapping.asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var isLoaded = false;
				EditorIsLoaded(prometheusIdentifier, ref isLoaded);
				return isLoaded;
			}

			if (!_unmanaged._contentFile2Index.TryGetValue(contentFileGuid, out var loadingIndex))
			{
				return false;
			}

			return IsSuccessfullyLoaded(_unmanaged._contentFileLoads[loadingIndex]);
		}

		public void StartAssetLoading(PrometheusIdentifier prometheusIdentifier, Priority priority = Priority.Normal)
		{
			StartAssetLoading(prometheusIdentifier, (byte)priority);
		}

		public void StartAssetLoading(PrometheusIdentifier prometheusIdentifier, byte priority)
		{
			if (!_prometheusMapping.asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var availableInEditor = false;
				EditorIsAssetAvailable(prometheusIdentifier, ref availableInEditor);
				if (!availableInEditor)
				{
					Debug.LogError($"Asset {prometheusIdentifier.assetGuid}[{prometheusIdentifier.localIdentifier}] not found in Prometheus");
				}
				return;
			}

			_unmanaged.StartLoading(contentFileGuid, priority);
		}

		public void StartAssetUnloading(PrometheusIdentifier prometheusIdentifier, Priority priority = Priority.Normal)
		{
			StartAssetUnloading(prometheusIdentifier, (byte)priority);
		}

		public void StartAssetUnloading(PrometheusIdentifier prometheusIdentifier, byte priority)
		{
			if (!_prometheusMapping.asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var availableInEditor = false;
				EditorIsAssetAvailable(prometheusIdentifier, ref availableInEditor);
				if (!availableInEditor)
				{
					Debug.LogError($"Asset {prometheusIdentifier.assetGuid}[{prometheusIdentifier.localIdentifier}] not found in Prometheus");
				}
				return;
			}

			_unmanaged.StartUnloading(contentFileGuid, priority);
		}

		public bool CanLoadAsset(PrometheusIdentifier prometheusIdentifier)
		{
			if (!_prometheusMapping.asset2ContentFile.ContainsKey(prometheusIdentifier))
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

			EditorUpdate();
		}

		static bool IsLoaded(in ContentFileLoad load)
		{
			return load.State is State.Loaded or State.ErrorArchive or State.ErrorContentFiles;
		}

		static bool IsSuccessfullyLoaded(in ContentFileLoad load)
		{
			return load.State is State.Loaded;
		}

		static PrometheusMapping LoadContentFilesData()
		{
			var mappingPath = PrometheusPersistence.MappingsFilePath;

			PrometheusMapping prometheusData;
			if (PrometheusSettings.Instance.useBuildData)
			{
				if (File.Exists(mappingPath))
				{
					prometheusData = PrometheusMapping.Deserialize(mappingPath, Allocator.Domain);
				}
				else
				{
					Debug.LogError("Cannot find Prometheus mapping file so build data is not available");
					prometheusData = PrometheusMapping.Fresh(Allocator.Domain);
				}
			}
			else
			{
				prometheusData = PrometheusMapping.Fresh(Allocator.Domain);
			}

			return prometheusData;
		}

		public enum Priority : byte
		{
			Background = 8,
			Low = 32,
			Normal = 64,
			High = 128,
			Urgent = 240,
		}
	}

	static class PrometheusExt
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

		public static byte Above(this PrometheusLoader.Priority priority, byte above = 1)
		{
			var newPriority = (byte)priority + (int)above;
			return (byte)math.min(newPriority, byte.MaxValue);
		}

		public static byte Below(this PrometheusLoader.Priority priority, byte below = 1)
		{
			var newPriority = (byte)priority - (int)below;
			return (byte)math.max(newPriority, byte.MinValue);
		}
	}
}
