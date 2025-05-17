using System;
using KVD.Utils.DataStructures;
using KVD.Utils.Extensions;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KVD.Prometheus
{
	public partial class PrometheusLoader
	{
		Callback[] _callbacks;
		UnsafeArray<LoadingTaskData> _loadingTasks;
		UnsafeArray<byte> _loadingTasksVersion;
		UnsafeBitmask _loadingTasksMask;
		UnsafeBitmask _waitingTasksMask;

		public Option<LoadingTaskHandle> LoadAssetAsync(in PrometheusIdentifier prometheusIdentifier)
		{
			return LoadAssetAsync(prometheusIdentifier, null, false);
		}

		public Option<LoadingTaskHandle> LoadAssetAsync(in PrometheusIdentifier prometheusIdentifier, Callback callback, bool delayedCallbacks)
		{
			if (!_prometheusMapping.Asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var editorHasAsset = false;
				var editorResult = Option<LoadingTaskHandle>.None;
				EditorLoadAssetAsync(prometheusIdentifier, callback, delayedCallbacks, ref editorHasAsset, ref editorResult);
				if (editorHasAsset)
				{
					return editorResult;
				}
				else
				{
					Debug.LogError($"Asset {prometheusIdentifier.assetGuid}[{prometheusIdentifier.localIdentifier}] not found in ContentMap");
					return Option<LoadingTaskHandle>.None;
				}
			}

			var loadingIndex = StartLoading(contentFileGuid);

			// Resize buffers if not enough space
			if (_loadingTasksMask.AllSet())
			{
				var newCapacity = _loadingTasks.Length*2;
				Array.Resize(ref _callbacks, (int)newCapacity);
				UnsafeArray<LoadingTaskData>.Resize(ref _loadingTasks, newCapacity);
				UnsafeArray<byte>.Resize(ref _loadingTasksVersion, newCapacity);
				_loadingTasksMask.EnsureCapacity(newCapacity);
				_waitingTasksMask.EnsureCapacity(newCapacity);
			}

			var loadingTaskIndex = (uint)_loadingTasksMask.FirstZero();
			_loadingTasksMask.Up(loadingTaskIndex);
			_loadingTasks[loadingTaskIndex] = new()
			{
				prometheusIdentifier = prometheusIdentifier,
			};

			var version = ++_loadingTasksVersion[loadingTaskIndex];
			var handle = LoadingTaskHandle.New(loadingTaskIndex, version);

			var contentFileLoad = _contentFileLoads[loadingIndex];
			if (!delayedCallbacks && IsLoaded(contentFileLoad))
			{
				callback?.Invoke(handle);
			}
			else
			{
				_callbacks[loadingTaskIndex] = callback;
				_waitingTasksMask.Up(loadingTaskIndex);
			}

			return Option<LoadingTaskHandle>.Some(handle);
		}

		public void UnloadAssetAsync(ref LoadingTaskHandle handle)
		{
#if UNITY_EDITOR
			if (handle.IsEditorLoading)
			{
				EditorUnloadAssetAsync(ref handle);
				return;
			}
#endif
			// Always override handle with canceled one
			var originalHandle = handle;
			handle = handle.MakeCancelled();

			if (CheckHandle(originalHandle, false) == false)
			{
				return;
			}

			var loadingTaskId = handle.loadingTaskId;
			var loadingTaskData = _loadingTasks[loadingTaskId];

			// If not loaded then we are canceling the loading task
			if (_waitingTasksMask[loadingTaskId] && IsLoaded(loadingTaskData.prometheusIdentifier) == false)
			{
				_callbacks[loadingTaskId]?.Invoke(handle);
			}

			// Cleanup our data
			_callbacks[loadingTaskId] = null;
			_loadingTasks[loadingTaskId] = default;
			++_loadingTasksVersion[loadingTaskId];
			_loadingTasksMask.Down(loadingTaskId);
			_waitingTasksMask.Down(loadingTaskId);

			StartAssetUnloading(loadingTaskData.prometheusIdentifier);
		}

		public (Option<T>, LoadResultState) Result<T>(in LoadingTaskHandle handle) where T : Object
		{
#if UNITY_EDITOR
			if (handle.IsEditorLoading)
			{
				var editorAsset = Option<T>.None;
				var editorState = LoadResultState.Invalid;
				EditorResult(handle, ref editorAsset, ref editorState);
				return (editorAsset, editorState);
			}
#endif

			if (CheckHandle(handle, true) == false)
			{
				return (Option<T>.None, LoadResultState.Invalid);
			}

			if (handle.IsCancelled)
			{
				return (Option<T>.None, LoadResultState.Cancelled);
			}

			var loadingTaskId = handle.loadingTaskId;
			var (asset, state) = Result(loadingTaskId, handle.IsCancelled);

			return asset.Deconstruct(out var assetValue) ?
				(Option<T>.Some((T)assetValue), state) :
				(Option<T>.None, state);
		}

		public PrometheusIdentifier RequestedAssetIdentifier(in LoadingTaskHandle handle)
		{
#if UNITY_EDITOR
			if (handle.IsEditorLoading)
			{
				var identifier = default(PrometheusIdentifier);
				EditorRequestedAssetIdentifier(handle, ref identifier);
				return identifier;
			}
#endif
			if (CheckHandle(handle, false) == false)
			{
				return default;
			}

			return _loadingTasks[handle.loadingTaskId].prometheusIdentifier;
		}

		public void AddCallback(in LoadingTaskHandle handle, Callback callback, bool delayedCallbacks)
		{
#if UNITY_EDITOR
			if (handle.IsEditorLoading)
			{
				EditorAddCallback(handle, callback, delayedCallbacks);
				return;
			}
#endif
			if (CheckHandle(handle, true) == false)
			{
				Debug.LogError($"Callback won't be called because added to invalid handle {handle}");
				return;
			}

			if (handle.IsCancelled)
			{
				callback.Invoke(handle);
				return;
			}

			var loadingTaskId = handle.loadingTaskId;
			var contentFileLoad = _contentFileLoads[loadingTaskId];

			if (!delayedCallbacks && IsLoaded(contentFileLoad))
			{
				callback?.Invoke(handle);
			}
			else
			{
				_callbacks[loadingTaskId] = callback;
				_waitingTasksMask.Up(loadingTaskId);
			}
		}

		void InitCallbacks()
		{
			_callbacks = new Callback[256];
			_loadingTasks = new(256, Allocator.Domain);
			_loadingTasksVersion = new(256, Allocator.Domain);
			_loadingTasksMask = new UnsafeBitmask(256, Allocator.Domain);
			_waitingTasksMask = new UnsafeBitmask(256, Allocator.Domain);
		}

		void UpdateCallbacks()
		{
			foreach (var loadingTaskIndex in _waitingTasksMask.EnumerateOnes())
			{
				var contentFileLoad = _contentFileLoads[loadingTaskIndex];
				if (IsLoaded(contentFileLoad) == false)
				{
					continue;
				}

				var handle = LoadingTaskHandle.New(loadingTaskIndex, _loadingTasksVersion[loadingTaskIndex]);
				_callbacks[loadingTaskIndex]?.Invoke(handle);
				_callbacks[loadingTaskIndex] = null;
				_waitingTasksMask.Down(loadingTaskIndex);
			}
		}

		(Option<Object>, LoadResultState) Result(uint loadingTaskId, bool withCancelledState)
		{
			var loadingTaskData = _loadingTasks[loadingTaskId];
			var contentFileGuid = _prometheusMapping.Asset2ContentFile[loadingTaskData.prometheusIdentifier];
			var loadingIndex = _contentFile2Index[contentFileGuid];
			ref var load = ref _contentFileLoads[loadingIndex];

			if (IsLoaded(load) == false)
			{
				if (withCancelledState)
				{
					return (Option<Object>.None, LoadResultState.Cancelled);
				}
				else
				{
					return (Option<Object>.None, LoadResultState.InProgress);
				}
			}

			return IsSuccessfullyLoaded(load) ?
				(Option<Object>.Some(load.contentFile.GetObject(_prometheusMapping.Asset2LocalIdentifier[loadingTaskData.prometheusIdentifier])), LoadResultState.Success) :
				(Option<Object>.None, LoadResultState.Fail);
		}

		bool CheckHandle(in LoadingTaskHandle handle, bool allowCancelled)
		{
#if UNITY_EDITOR
			if (handle.IsEditorLoading)
			{
				Debug.LogError("Editor handle runtime code path");
				return false;
			}
#endif

			if (!handle.IsValid)
			{
				Debug.LogError("Handle is not valid");
				return false;
			}

			var cancelled = handle.IsCancelled;
			if ((allowCancelled && !cancelled) | !allowCancelled)
			{
				var loadingTaskId = handle.loadingTaskId;
				if (!_loadingTasksMask[loadingTaskId])
				{
					Debug.LogError($"Loading task {handle} not found");
					return false;
				}

				if (handle.version != _loadingTasksVersion[loadingTaskId])
				{
					Debug.LogError($"Loading task {handle} version mismatch {handle.version} != {_loadingTasksVersion[loadingTaskId]}");
					return false;
				}

				if (cancelled)
				{
					Debug.LogError($"Loading task {handle} is cancelled");
					return false;
				}
			}

			return true;
		}

		public delegate void Callback(in LoadingTaskHandle handle);

		public enum LoadResultState
		{
			/// <summary>
			/// Loading is in invalid state
			/// </summary>
			Invalid,
			/// <summary>
			/// Loading failed, no archive or content file found
			/// </summary>
			Fail,
			/// <summary>
			/// User cancelled the loading
			/// </summary>
			Cancelled,
			/// <summary>
			/// Asset loaded successfully
			/// </summary>
			Success,
			/// <summary>
			/// Still loading
			/// </summary>
			InProgress,
		}

		public readonly struct LoadingTaskHandle : IEquatable<LoadingTaskHandle>
		{
			readonly StateMask _stateMask;
			readonly Flags _flags;
			public readonly byte version;
			public readonly uint loadingTaskId;

			public bool IsValid => _stateMask.HasFlagFast(StateMask.Valid);
			public bool IsCancelled => _stateMask.HasFlagFast(StateMask.Cancelled);

			internal bool IsEditorLoading => _flags.HasFlagFast(Flags.EditorLoading);
			public PrometheusReference Reference => new PrometheusReference(Instance.RequestedAssetIdentifier(this));

			LoadingTaskHandle(StateMask state, uint loadingTaskId, byte version, Flags flags)
			{
				_stateMask = state;
				this.loadingTaskId = loadingTaskId;
				this.version = version;
				_flags = flags;
			}

			internal static LoadingTaskHandle New(uint loadingTaskId, byte version)
			{
				return new(StateMask.Valid, loadingTaskId, version, Flags.None);
			}

			internal static LoadingTaskHandle Editor(uint loadingTaskId, byte version)
			{
				return new(StateMask.Valid, loadingTaskId, version, Flags.EditorLoading);
			}

			internal LoadingTaskHandle MakeCancelled()
			{
				return new(_stateMask | StateMask.Cancelled, loadingTaskId, version, _flags);
			}

			[Flags]
			enum StateMask : byte
			{
				Invalid = 0,
				Valid = 1 << 0,
				Cancelled = 1 << 1,
			}

			[Flags]
			enum Flags : byte
			{
				None = 0,
				EditorLoading = 1 << 0,
			}

			public bool Equals(LoadingTaskHandle other)
			{
				return version == other.version && loadingTaskId == other.loadingTaskId;
			}

			public override string ToString()
			{
				return $"LoadingTaskHandle({loadingTaskId}:{version} state={_stateMask} flags={_flags})";
			}
		}

		public struct LoadingTaskData
		{
			public PrometheusIdentifier prometheusIdentifier;
		}
	}
}
