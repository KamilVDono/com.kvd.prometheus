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
		const uint PreAllocSize = 256;

		Callback[] _callbacks;
		UnsafeArray<LoadingTaskData> _loadingTasks;
		UnsafeArray<byte> _loadingTasksVersion;
		UnsafeBitmask _loadingTasksMask;
		UnsafeBitmask _waitingTasksMask;

		public LoadingTaskHandle LoadAssetAsync(in PrometheusIdentifier prometheusIdentifier, in CallbackSetup callbackSetup, Priority priority = Priority.Normal)
		{
			return LoadAssetAsync(prometheusIdentifier, callbackSetup, (byte)priority);
		}

		public LoadingTaskHandle LoadAssetAsync(in PrometheusIdentifier prometheusIdentifier, in CallbackSetup callbackSetup, byte priority)
		{
			if (!_prometheusMapping.asset2ContentFile.TryGetValue(prometheusIdentifier, out var contentFileGuid))
			{
				var editorHasAsset = false;
				var editorResult = default(LoadingTaskHandle);
				EditorLoadAssetAsync(prometheusIdentifier, callbackSetup.callback, callbackSetup.delayedCallbacks, ref editorHasAsset, ref editorResult);
				if (editorHasAsset)
				{
					return editorResult;
				}
				else
				{
					Debug.LogError($"Asset {prometheusIdentifier.assetGuid}[{prometheusIdentifier.localIdentifier}] not found in ContentMap");
					return default;
				}
			}

			var loadingIndex = _unmanaged.StartLoading(contentFileGuid, priority);

			// Resize buffers if not enough space
			if (_loadingTasksMask.AllSet())
			{
				var newCapacity = _loadingTasks.Length*2;
				Array.Resize(ref _callbacks, (int)newCapacity);
				UnsafeArray<LoadingTaskData>.Resize(ref _loadingTasks, newCapacity);
				UnsafeArray<byte>.Resize(ref _loadingTasksVersion, newCapacity);
				_loadingTasksMask.EnsureElementsCapacity(newCapacity);
				_waitingTasksMask.EnsureElementsCapacity(newCapacity);
			}

			var loadingTaskIndex = (uint)_loadingTasksMask.FirstZero();
			_loadingTasksMask.Up(loadingTaskIndex);
			_loadingTasks[loadingTaskIndex] = new()
			{
				prometheusIdentifier = prometheusIdentifier,
			};

			var version = ++_loadingTasksVersion[loadingTaskIndex];
			var handle = LoadingTaskHandle.New(loadingTaskIndex, version);

			var contentFileLoad = _unmanaged._contentFileLoads[loadingIndex];
			if (!callbackSetup.delayedCallbacks && IsLoaded(contentFileLoad))
			{
				callbackSetup.callback?.Invoke(handle);
			}
			else
			{
				_callbacks[loadingTaskIndex] = callbackSetup.callback;
				_waitingTasksMask.Up(loadingTaskIndex);
			}

			return handle;
		}

		public void UnloadAssetAsync(ref LoadingTaskHandle handle)
		{
#if UNITY_EDITOR
			if (handle.IsEditorLoading)
			{
				var wasUnloaded = false;
				EditorUnloadAssetAsync(ref handle, false, ref wasUnloaded);
				return;
			}
#endif
			// Always override handle with canceled one
			var originalHandle = handle;
			var cancelledHandle = handle.MakeCancelled();
			// remember to override the handle (passed as ref so will override the original)
			handle = cancelledHandle;

			if (CheckHandle(originalHandle, false) == false)
			{
				return;
			}

			var loadingTaskId = cancelledHandle.loadingTaskId;
			var loadingTaskData = _loadingTasks[loadingTaskId];

			// If not loaded, then we are canceling the loading task
			if (_waitingTasksMask[loadingTaskId] && IsLoaded(loadingTaskData.prometheusIdentifier) == false)
			{
				_callbacks[loadingTaskId]?.Invoke(cancelledHandle);
			}

			// Cleanup our data
			_callbacks[loadingTaskId] = null;
			_loadingTasks[loadingTaskId] = default;
			++_loadingTasksVersion[loadingTaskId];
			_loadingTasksMask.Down(loadingTaskId);
			_waitingTasksMask.Down(loadingTaskId);

			StartAssetUnloading(loadingTaskData.prometheusIdentifier);
		}

		public bool TryUnloadAssetAsync(ref LoadingTaskHandle handle)
		{
#if UNITY_EDITOR
			if (handle.IsEditorLoading)
			{
				var wasUnloaded = false;
				EditorUnloadAssetAsync(ref handle, true, ref wasUnloaded);
				return wasUnloaded;
			}
#endif
			// Always override handle with canceled one
			var originalHandle = handle;
			var cancelledHandle = handle.MakeCancelled();
			// remember to override the handle (passed as ref so will override the original)
			handle = cancelledHandle;

			if (CheckHandle(originalHandle, true) == false)
			{
				return false;
			}

			var loadingTaskId = cancelledHandle.loadingTaskId;
			var loadingTaskData = _loadingTasks[loadingTaskId];

			// If not loaded, then we are canceling the loading task
			if (_waitingTasksMask[loadingTaskId] && IsLoaded(loadingTaskData.prometheusIdentifier) == false)
			{
				_callbacks[loadingTaskId]?.Invoke(cancelledHandle);
			}

			// Cleanup our data
			_callbacks[loadingTaskId] = null;
			_loadingTasks[loadingTaskId] = default;
			++_loadingTasksVersion[loadingTaskId];
			_loadingTasksMask.Down(loadingTaskId);
			_waitingTasksMask.Down(loadingTaskId);

			StartAssetUnloading(loadingTaskData.prometheusIdentifier);

			return true;
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

		public void AddCallback(in LoadingTaskHandle handle, in CallbackSetup callbackSetup)
		{
#if UNITY_EDITOR
			if (handle.IsEditorLoading)
			{
				EditorAddCallback(handle, callbackSetup.callback, callbackSetup.delayedCallbacks);
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
				callbackSetup.callback.Invoke(handle);
				return;
			}

			var loadingTaskId = handle.loadingTaskId;
			var contentFileLoad = _unmanaged._contentFileLoads[loadingTaskId];

			if (!callbackSetup.delayedCallbacks && IsLoaded(contentFileLoad))
			{
				callbackSetup.callback.Invoke(handle);
			}
			else
			{
				if (_callbacks[loadingTaskId] == null)
				{
					_callbacks[loadingTaskId] = callbackSetup.callback;
				}
				else
				{
					var oldCallback = _callbacks[loadingTaskId];
					_callbacks[loadingTaskId] = oldCallback + callbackSetup.callback;
				}
				_waitingTasksMask.Up(loadingTaskId);
			}
		}

		void InitCallbacks()
		{
			_callbacks = new Callback[PreAllocSize];
			_loadingTasks = new(PreAllocSize, Allocator.Domain);
			_loadingTasksVersion = new(PreAllocSize, Allocator.Domain);
			_loadingTasksMask = new UnsafeBitmask(PreAllocSize, Allocator.Domain);
			_waitingTasksMask = new UnsafeBitmask(PreAllocSize, Allocator.Domain);
		}

		void UpdateCallbacks()
		{
			foreach (var loadingTaskIndex in _waitingTasksMask.EnumerateOnes())
			{
				var contentFileLoad = _unmanaged._contentFileLoads[loadingTaskIndex];
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
			var contentFileGuid = _prometheusMapping.asset2ContentFile[loadingTaskData.prometheusIdentifier];
			var loadingIndex = _unmanaged._contentFile2Index[contentFileGuid];
			ref var load = ref _unmanaged._contentFileLoads[loadingIndex];

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
				(Option<Object>.Some(load.contentFile.GetObject(_prometheusMapping.asset2LocalIdentifier[loadingTaskData.prometheusIdentifier])), LoadResultState.Success) :
				(Option<Object>.None, LoadResultState.Fail);
		}

		PrometheusIdentifier RequestedAssetIdentifier(in LoadingTaskHandle handle)
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

			internal bool IsEditorLoading => _flags.HasFlagFast(Flags.EditorLoading);

			public bool IsValid => _stateMask.HasFlagFast(StateMask.Valid);
			public bool IsCancelled => _stateMask.HasFlagFast(StateMask.Cancelled);

			public PrometheusIdentifier Identifier => Instance.RequestedAssetIdentifier(this);
			public PrometheusReference Reference => new PrometheusReference(Identifier);

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

		public readonly ref struct CallbackSetup
		{
			public readonly Callback callback;
			public readonly bool delayedCallbacks;

			public static CallbackSetup Delayed(Callback callback) => new(callback, true);
			public static CallbackSetup Immediate(Callback callback) => new(callback, false);

			CallbackSetup(Callback callback, bool delayedCallbacks)
			{
				this.callback = callback;
				this.delayedCallbacks = delayedCallbacks;
			}
		}
	}
}
