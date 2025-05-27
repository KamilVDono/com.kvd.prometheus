using System;
using System.Collections.Generic;
using System.Diagnostics;
using KVD.Utils.DataStructures;
using Unity.Collections;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace KVD.Prometheus
{
	public partial class PrometheusLoader
	{
#if UNITY_EDITOR
		Dictionary<PrometheusIdentifier, UnityEditor.AssetDatabaseLoadOperation> _editorLoadings = new Dictionary<PrometheusIdentifier, UnityEditor.AssetDatabaseLoadOperation>();

		Callback[] _editorCallbacks;
		UnsafeArray<LoadingTaskData> _editorLoadingTasks;
		UnsafeArray<byte> _editorLoadingTasksVersion;
		UnsafeBitmask _editorLoadingTasksMask;
		UnsafeBitmask _editorWaitingTasksMask;

		public static Func<PrometheusIdentifier, bool> IsAssetAvailableFunc;
#endif

		[Conditional("UNITY_EDITOR")]
		void EditorIsAssetAvailable(in PrometheusIdentifier prometheusIdentifier, ref bool hasAsset)
		{
			hasAsset = false;
#if UNITY_EDITOR
			if (PrometheusSettings.Instance.useBuildData)
			{
				return;
			}
			var isPresent = false;
			CheckIfPresentInPrometheusAssets(prometheusIdentifier, ref isPresent);
			if (!isPresent)
			{
				return;
			}

			if (!_editorLoadings.TryGetValue(prometheusIdentifier, out var loading))
			{
				var path = UnityEditor.AssetDatabase.GUIDToAssetPath(prometheusIdentifier.assetGuid.ToString("N"));
				if (string.IsNullOrWhiteSpace(path))
				{
					return;
				}
				loading = UnityEditor.AssetDatabase.LoadObjectAsync(path, prometheusIdentifier.localIdentifier);
				_editorLoadings.Add(prometheusIdentifier, loading);
			}
			hasAsset = true;
#endif
		}

		[Conditional("UNITY_EDITOR")]
		void EditorGetAsset<T>(in PrometheusIdentifier prometheusIdentifier, ref bool hasAsset, ref Option<T> asset) where T : Object
		{
			asset = Option<T>.None;
			hasAsset = false;
#if UNITY_EDITOR
			if (PrometheusSettings.Instance.useBuildData)
			{
				return;
			}
			var isPresent = false;
			CheckIfPresentInPrometheusAssets(prometheusIdentifier, ref isPresent);
			if (!isPresent)
			{
				return;
			}

			if (!_editorLoadings.TryGetValue(prometheusIdentifier, out var loading))
			{
				var path = UnityEditor.AssetDatabase.GUIDToAssetPath(prometheusIdentifier.assetGuid.ToString("N"));
				if (string.IsNullOrWhiteSpace(path))
				{
					return;
				}
				loading = UnityEditor.AssetDatabase.LoadObjectAsync(path, prometheusIdentifier.localIdentifier);
				_editorLoadings.Add(prometheusIdentifier, loading);
			}
			hasAsset = true;
			if (loading.isDone)
			{
				if (loading.LoadedObject is T t)
				{
					asset = Option<T>.Some(t);
				}
				else
				{
					Debug.LogWarning($"Failed to load asset {prometheusIdentifier.assetGuid}[{prometheusIdentifier.localIdentifier}] as type {typeof(T)}");
				}
			}
#endif
		}

		[Conditional("UNITY_EDITOR")]
		void EditorForceGetAsset<T>(in PrometheusIdentifier prometheusIdentifier, ref bool hasAsset, ref Option<T> asset) where T : Object
		{
			asset = Option<T>.None;
			hasAsset = false;
#if UNITY_EDITOR
			if (PrometheusSettings.Instance.useBuildData)
			{
				return;
			}
			var isPresent = false;
			CheckIfPresentInPrometheusAssets(prometheusIdentifier, ref isPresent);
			if (!isPresent)
			{
				return;
			}

			if (!_editorLoadings.TryGetValue(prometheusIdentifier, out var loading))
			{
				var path = UnityEditor.AssetDatabase.GUIDToAssetPath(prometheusIdentifier.assetGuid.ToString("N"));
				if (string.IsNullOrWhiteSpace(path))
				{
					return;
				}
				loading = UnityEditor.AssetDatabase.LoadObjectAsync(path, prometheusIdentifier.localIdentifier);
				_editorLoadings.Add(prometheusIdentifier, loading);
			}

			hasAsset = true;

			var loadedObject = default(Object);
			if (loading.isDone)
			{
				loadedObject = loading.LoadedObject;
			}
			else
			{
				var path = UnityEditor.AssetDatabase.GUIDToAssetPath(prometheusIdentifier.assetGuid.ToString("N"));
				if (string.IsNullOrWhiteSpace(path))
				{
					return;
				}
				var allAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
				foreach (var loadedAsset in allAssets)
				{
					UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(loadedAsset, out _, out var localIdentifier);
					if (localIdentifier == prometheusIdentifier.localIdentifier)
					{
						loadedObject = loadedAsset;
						break;
					}
				}
			}

			if (loadedObject is T castedAsset)
			{
				asset = Option<T>.Some(castedAsset);
				hasAsset = true;
			}
			else
			{
				Debug.LogWarning($"Failed to load asset {prometheusIdentifier.assetGuid}[{prometheusIdentifier.localIdentifier}] as type {typeof(T)}");
			}
#endif
		}

		[Conditional("UNITY_EDITOR")]
		void EditorIsActive(in PrometheusIdentifier prometheusIdentifier, ref bool isActive)
		{
			isActive = false;
#if UNITY_EDITOR
			if (PrometheusSettings.Instance.useBuildData)
			{
				return;
			}
			var isPresent = false;
			CheckIfPresentInPrometheusAssets(prometheusIdentifier, ref isPresent);
			if (!isPresent)
			{
				return;
			}

			isActive = _editorLoadings.ContainsKey(prometheusIdentifier);
#endif
		}

		[Conditional("UNITY_EDITOR")]
		void EditorIsLoading(in PrometheusIdentifier prometheusIdentifier, ref bool isLoading)
		{
			isLoading = false;
#if UNITY_EDITOR
			if (PrometheusSettings.Instance.useBuildData)
			{
				return;
			}
			var isPresent = false;
			CheckIfPresentInPrometheusAssets(prometheusIdentifier, ref isPresent);
			if (!isPresent)
			{
				return;
			}

			if (_editorLoadings.TryGetValue(prometheusIdentifier, out var loading))
			{
				isLoading = !loading.isDone;
			}
#endif
		}

		[Conditional("UNITY_EDITOR")]
		void EditorIsLoaded(in PrometheusIdentifier prometheusIdentifier, ref bool isLoaded)
		{
			isLoaded = false;
#if UNITY_EDITOR
			if (PrometheusSettings.Instance.useBuildData)
			{
				return;
			}
			var isPresent = false;
			CheckIfPresentInPrometheusAssets(prometheusIdentifier, ref isPresent);
			if (!isPresent)
			{
				return;
			}

			if (_editorLoadings.TryGetValue(prometheusIdentifier, out var loading))
			{
				isLoaded = loading.isDone;
			}
#endif
		}

		[Conditional("UNITY_EDITOR")]
		void EditorInitCallbacks()
		{
#if UNITY_EDITOR
			_editorCallbacks = new Callback[256];
			_editorLoadingTasks = new(256, Allocator.Domain);
			_editorLoadingTasksVersion = new(256, Allocator.Domain);
			_editorLoadingTasksMask = new UnsafeBitmask(256, Allocator.Domain);
			_editorWaitingTasksMask = new UnsafeBitmask(256, Allocator.Domain);
#endif
		}

		[Conditional("UNITY_EDITOR")]
		void EditorLoadAssetAsync(in PrometheusIdentifier prometheusIdentifier, Callback callback, bool delayedCallbacks, ref bool hasAsset, ref Option<LoadingTaskHandle> loadingHandle)
		{
			hasAsset = false;
			loadingHandle = Option<LoadingTaskHandle>.None;
#if UNITY_EDITOR
			if (PrometheusSettings.Instance.useBuildData)
			{
				return;
			}
			var isPresent = false;
			CheckIfPresentInPrometheusAssets(prometheusIdentifier, ref isPresent);
			if (!isPresent)
			{
				return;
			}

			var asset = Option<Object>.None;
			EditorGetAsset(prometheusIdentifier, ref hasAsset, ref asset);
			if (!hasAsset)
			{
				return;
			}

			// Resize buffers if not enough space
			if (_editorLoadingTasksMask.AllSet())
			{
				var newCapacity = _editorLoadingTasks.Length*2;
				Array.Resize(ref _editorCallbacks, (int)newCapacity);
				UnsafeArray<LoadingTaskData>.Resize(ref _editorLoadingTasks, newCapacity);
				UnsafeArray<byte>.Resize(ref _editorLoadingTasksVersion, newCapacity);
				_editorLoadingTasksMask.EnsureCapacity(newCapacity);
				_editorWaitingTasksMask.EnsureCapacity(newCapacity);
			}

			var loadingTaskIndex = (uint)_editorLoadingTasksMask.FirstZero();
			_editorLoadingTasksMask.Up(loadingTaskIndex);
			_editorWaitingTasksMask.Up(loadingTaskIndex);
			_editorLoadingTasks[loadingTaskIndex] = new()
			{
				prometheusIdentifier = prometheusIdentifier,
			};

			var version = ++_editorLoadingTasksVersion[loadingTaskIndex];
			var handle = LoadingTaskHandle.Editor(loadingTaskIndex, version);

			if (!delayedCallbacks && asset.HasValue)
			{
				callback?.Invoke(handle);
			}
			else
			{
				_editorCallbacks[loadingTaskIndex] = callback;
			}

			loadingHandle = Option<LoadingTaskHandle>.Some(handle);
#endif
		}

		[Conditional("UNITY_EDITOR")]
		void EditorUnloadAssetAsync(ref LoadingTaskHandle handle)
		{
#if UNITY_EDITOR
			if (PrometheusSettings.Instance.useBuildData)
			{
				return;
			}
			// Always invalidate the handle
			var originalHandle = handle;
			handle = handle.MakeCancelled();

			var isHandleValid = false;
			EditorCheckHandle(originalHandle, false, ref isHandleValid);
			if (isHandleValid == false)
			{
				return;
			}

			var loadingTaskId = handle.loadingTaskId;
			var loadingTaskData = _editorLoadingTasks[loadingTaskId];

			var isLoaded = false;
			EditorIsLoaded(loadingTaskData.prometheusIdentifier, ref isLoaded);

			++_editorLoadingTasksVersion[loadingTaskId];
			_editorLoadingTasksMask.Down(loadingTaskId);
			_editorWaitingTasksMask.Down(loadingTaskId);

			// If not loaded then we are canceling the loading task
			if (isLoaded == false)
			{
				_editorCallbacks[loadingTaskId]?.Invoke(handle);
			}

			// Cleanup our data
			_editorCallbacks[loadingTaskId] = null;
			_editorLoadingTasks[loadingTaskId] = default;
#endif
		}

		[Conditional("UNITY_EDITOR")]
		void EditorCheckHandle(in LoadingTaskHandle handle, bool allowCancelled, ref bool isValid)
		{
			isValid = false;
#if UNITY_EDITOR
			if (PrometheusSettings.Instance.useBuildData)
			{
				return;
			}
			if (!handle.IsValid)
			{
				Debug.LogError("Handle is not valid");
				return;
			}

			var cancelled = handle.IsCancelled;
			if ((allowCancelled && !cancelled) | !allowCancelled)
			{
				var loadingTaskId = handle.loadingTaskId;
				if (!_editorLoadingTasksMask[loadingTaskId])
				{
					Debug.LogError($"Loading task {handle} not found");
					return;
				}

				if (handle.version != _editorLoadingTasksVersion[loadingTaskId])
				{
					Debug.LogError($"Loading task {handle} version mismatch {handle.version} != {_editorLoadingTasksVersion[loadingTaskId]}");
					return;
				}

				if (cancelled)
				{
					Debug.LogError($"Loading task {handle} is cancelled");
					return;
				}
			}

			isValid = true;
#endif
		}

		[Conditional("UNITY_EDITOR")]
		void EditorResult<T>(in LoadingTaskHandle handle, ref Option<T> asset, ref LoadResultState result) where T : Object
		{
			asset = Option<T>.None;
			result = LoadResultState.Invalid;
#if UNITY_EDITOR
			if (PrometheusSettings.Instance.useBuildData)
			{
				return;
			}
			var isValidHandle = false;
			EditorCheckHandle(handle, true, ref isValidHandle);
			if (isValidHandle == false)
			{
				return;
			}

			if (handle.IsCancelled)
			{
				result = LoadResultState.Cancelled;
				return;
			}

			var loadingTaskId = handle.loadingTaskId;
			var loadingTaskData = _editorLoadingTasks[loadingTaskId];

			var hasAsset = false;
			EditorGetAsset(loadingTaskData.prometheusIdentifier, ref hasAsset, ref asset);
			if (asset.HasValue)
			{
				result = LoadResultState.Success;
			}
			else
			{
				result = LoadResultState.InProgress;
			}
#endif
		}

		[Conditional("UNITY_EDITOR")]
		void EditorRequestedAssetIdentifier(in LoadingTaskHandle handle, ref PrometheusIdentifier result)
		{
			result = default;
#if UNITY_EDITOR
			if (PrometheusSettings.Instance.useBuildData)
			{
				return;
			}
			var isValidHandle = false;
			EditorCheckHandle(handle, false, ref isValidHandle);
			if (isValidHandle)
			{
				result = _editorLoadingTasks[handle.loadingTaskId].prometheusIdentifier;
			}
#endif
		}

		[Conditional("UNITY_EDITOR")]
		void EditorAddCallback(in LoadingTaskHandle handle, Callback callback, bool delayedCallbacks)
		{
#if UNITY_EDITOR
			if (PrometheusSettings.Instance.useBuildData)
			{
				return;
			}
			var isValidHandle = false;
			EditorCheckHandle(handle, true, ref isValidHandle);
			if (isValidHandle)
			{
				if (handle.IsCancelled)
				{
					callback.Invoke(handle);
					return;
				}

				var loadingTaskId = handle.loadingTaskId;
				var contentFileLoad = _editorLoadingTasks[loadingTaskId];

				var asset = Option<Object>.None;
				var hasAsset = false;
				EditorGetAsset(contentFileLoad.prometheusIdentifier, ref hasAsset, ref asset);
				if (!hasAsset)
				{
					return;
				}

				if (!delayedCallbacks && asset.HasValue)
				{
					callback?.Invoke(handle);
				}
				else
				{
					_editorCallbacks[loadingTaskId] = callback;
					_editorWaitingTasksMask.Up(loadingTaskId);
				}
			}
			else
			{
				Debug.LogError($"Callback won't be called because added to invalid handle {handle}");
			}
#endif
		}

		[Conditional("UNITY_EDITOR")]
		void CheckIfPresentInPrometheusAssets(in PrometheusIdentifier prometheusIdentifier, ref bool hasAsset)
		{
			hasAsset = false;
#if UNITY_EDITOR
			if (PrometheusSettings.Instance.useBuildData)
			{
				return;
			}
			if (IsAssetAvailableFunc != null)
			{
				var isPresent = IsAssetAvailableFunc(prometheusIdentifier);
				if (!isPresent)
				{
					hasAsset = false;
					var reference = new PrometheusReference(prometheusIdentifier.assetGuid, prometheusIdentifier.localIdentifier);
					Debug.LogError($"{prometheusIdentifier} is not present in PrometheusAssets, please add it", reference.EditorAsset<Object>());
				}
				else
				{
					hasAsset = true;
				}
			}
#endif
		}

		[Conditional("UNITY_EDITOR")]
		void EditorUpdateCallbacks()
		{
#if UNITY_EDITOR
			foreach (var loadingTaskIndex in _editorWaitingTasksMask.EnumerateOnes())
			{
				var asset = Option<Object>.None;
				var state = LoadResultState.Invalid;

				var loadingTaskData = _editorLoadingTasks[loadingTaskIndex];

				var hasAsset = false;
				EditorGetAsset(loadingTaskData.prometheusIdentifier, ref hasAsset, ref asset);
				if (asset.HasValue)
				{
					var handle = LoadingTaskHandle.Editor(loadingTaskIndex, _editorLoadingTasksVersion[loadingTaskIndex]);
					_editorCallbacks[loadingTaskIndex]?.Invoke(handle);
					_editorCallbacks[loadingTaskIndex] = null;
					_editorWaitingTasksMask.Down(loadingTaskIndex);
				}
			}
#endif
		}
	}
}
