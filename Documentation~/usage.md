# Usage

[Table of contents](index.md)

**The main API is accessible via `PrometheusLoader.Instance`.**

**Priority:** A higher value means higher priority. Higher priority means the asset will be streamed/processed first, but it does _not_ guarantee it will be loaded first. For example, if you start loading a lightweight mesh with priority 1 and a heavy prefab with priority 200, there is a high probability that the mesh will finish loading first.

## Common API

### Data Structures

`Option<T>` - Represents a value of type `T` or None, similar to a nullable type. Forces the user to handle the possibility of None.

`PrometheusIdentifier` - An immutable identifier for an asset. Preferred for runtime usage.

`PrometheusReference` - A soft reference to an asset, similar to `PrometheusIdentifier` but mutable and can be set up from the editor.

### Streaming

#### Methods

`void StartAssetLoading(identifier, priority)` - Increments the reference count of the asset with the given identifier and starts loading if the previous reference count was 0.

`void StartAssetUnloading(identifier, priority)` - Decrements the reference count of the asset with the given identifier and starts unloading if the reference count is now 0.

`Option<T> GetAsset<T>(identifier) where T : Object` - If the asset related to the identifier is successfully loaded, returns the asset; otherwise, returns None.

`Option<T> ForceGetAsset<T>(identifier) where T : Object` - If the asset is not loaded, synchronously completes loading, then operates like `GetAsset<T>`.

### Queries

#### Methods

`bool IsActive(in identifier)` - Returns true if the reference count is greater than 0; otherwise, false. In other words, true if you called `StartAssetLoading` more times than `StartAssetUnloading` or it is a dependency of another active asset.

`bool IsLoading(in identifier)` - Returns true if the asset is in any loading state; false if loaded or the reference count equals 0.

`bool IsLoaded(in identifier)` - Returns true if the asset is in any loaded state (successful or failed); otherwise, false.

`bool IsSuccessfullyLoaded(in identifier)` - Returns true if the asset is loaded and loading yielded success.

## Bursted API

By calling `PrometheusLoader.Instance.Unmanaged`, you get a reference to an unmanaged struct that provides an API for bursted operations. You can pass it as a pointer to a Job.

### Methods

`void StartAssetLoading(identifier, priority)` - Increments the reference count of the asset with the given identifier and starts loading if the previous reference count was 0.

`void StartAssetUnloading(identifier, priority)` - Decrements the reference count of the asset with the given identifier and starts unloading if the reference count is now 0.

## Callbacks

> **Note:** While callbacks may look simpler, in reality, they become more complex when cancellation is involved.

### Data Structures

`CallbackSetup` - Struct that holds callback setup. You can create two types:
- **Immediate** - Will be called immediately inside `LoadAssetAsync` if the asset is already loaded.
- **Delayed** - Even if the asset is already loaded, the callback will be called during the next `PreUpdate`. (Note: This does not necessarily mean the next frame; if you call loading at any point in the player loop before `PreUpdate`, it will be called in the same frame.)

`LoadingTaskHandle` - Represents asset loading, allows you to unload/cancel loading and query for the current state of loading. Handles have built-in safety checks, so it should not be possible to invalidate the state of loading.

`LoadResultState` - Enumeration for the current state of loading. Values are:
- **Invalid** - `LoadingTaskHandle` was invalid.
- **Fail** - Loading is done but the asset is not available (maybe a corrupted or missing asset file).
- **Cancelled** - Loading was cancelled.
- **Success** - Loading completed successfully.
- **InProgress** - Loading is still ongoing.

### Methods

`Option<LoadingTaskHandle> LoadAssetAsync(in identifier, in callbackSetup, priority)` - Starts asset loading. If the asset is available for streaming (is in build), returns `LoadingTaskHandle`; otherwise, returns None.

`void UnloadAssetAsync(ref handle)` - Unloads (or cancels loading if still loading) the asset. **An error will be printed if the handle is not in `Fail`, `Success`, or `InProgress` state.**

`bool TryUnloadAssetAsync(ref handle)` - Unloads (or cancels loading if still loading) the asset. Returns true if unloading succeeded; if the handle was already cancelled or invalid, returns false. **An error will be printed if the handle is `Invalid`.**

`(Option<T>, LoadResultState) Result<T>(in handle) where T : Object` - Checks the state of loading. Returns a pair with an option to the asset (same as `GetAsset`) and the loading state. You will probably call this from a callback.

`void AddCallback(in handle, in callbackSetup)` - Allows you to append an additional callback to existing loading. _If loading is cancelled, the callback will always be immediate._

### Cancellation

After calling `UnloadAssetAsync`, you will still get a callback, where `LoadResultState` will be `Cancelled`.

## Examples

### 1. Basic Asset Loading and Unloading

```csharp
using UnityEngine;
using KVD.Prometheus;

public class AssetUser : MonoBehaviour
{
	[SerializeField, PrometheusReferenceType(typeof(GameObject))]
	private PrometheusReference _assetReference;
	private GameObject _instance;

	void OnEnable()
	{
		// Start loading asset with priority 100
		PrometheusLoader.Instance.StartAssetLoading(_assetReference, 100);
	}

	void OnDisable()
	{
		// Unload asset when done
		PrometheusLoader.Instance.StartAssetUnloading(_assetReference, 100);
		if (_instance != null)
		{
			Destroy(_instance);
			_instance = null;
		}
	}

	void Update()
	{
		if (!_instance)
		{
			// Try to get loaded prefab and instantiate if available
			var prefabOption = PrometheusLoader.Instance.GetAsset<GameObject>(_assetReference);
			if (prefabOption.TryGetValue(out var prefab))
			{
				_instance = Instantiate(prefab);
			}
		}
	}
}
```

### 2. Asynchronous Loading with Callback

```csharp
using UnityEngine;
using KVD.Prometheus;

public class AsyncAssetUser : MonoBehaviour
{
	[SerializeField, PrometheusReferenceType(typeof(GameObject))]
	private PrometheusReference _assetReference;
	private PrometheusLoader.LoadingTaskHandle _handle;
	private GameObject _instance;

	void OnEnable()
	{
		var callbackSetup = PrometheusLoader.CallbackSetup.Delayed(OnAssetLoaded);
		// Start async loading
		_handle = PrometheusLoader.Instance.LoadAssetAsync(_assetReference, callbackSetup, PrometheusLoader.Priority.High);
	}

	void OnDisable()
	{
		// Unload or cancel loading
		if (_handle.IsValid)
		{
			PrometheusLoader.Instance.UnloadAssetAsync(ref _handle);
			_handle = default;
		}
		if (_instance != null)
		{
			Destroy(_instance);
			_instance = null;
		}
	}

	private void OnAssetLoaded(in PrometheusLoader.LoadingTaskHandle handle)
	{
		var (assetOption, state) = PrometheusLoader.Instance.Result<GameObject>(handle);
		if (state == PrometheusLoader.LoadResultState.Success && assetOption.TryGetValue(out var prefab))
		{
			_instance = Instantiate(prefab);
		}
		else
		{
			// Handle fail/cancel
		}
	}
}
```

### 3. Bursted API Usage (for Jobs)

```csharp
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using KVD.Utils.DataStructures;
using KVD.Prometheus;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

public class BurstedAssetUser : MonoBehaviour
{
	[SerializeField, PrometheusReferenceType(typeof(GameObject))]
	private PrometheusReference[] _assetReferences;

	unsafe void OnEnable()
	{
		var unmanaged = PrometheusLoader.Instance.UnmanagedApi;
		
		var identifiers = new UnsafeArray<PrometheusIdentifier>((uint)_assetReferences.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
		fixed (PrometheusReference* assetReferencesPtr = &_assetReferences[0])
		{
			// PrometheusReference and PrometheusIdentifier have the same memory layout
			UnsafeUtility.MemCpy(identifiers.Ptr, assetReferencesPtr, UnsafeUtility.SizeOf<PrometheusIdentifier>() * _assetReferences.Length);
		}

		new StartLoadsJob
		{
			identifiers = identifiers,
			prometheus = unmanaged
		}.Run();
		
		identifiers.Dispose();
	}
	
	unsafe void OnDisable()
	{
		var unmanaged = PrometheusLoader.Instance.UnmanagedApi;
		
		var identifiers = new UnsafeArray<PrometheusIdentifier>((uint)_assetReferences.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
		fixed (PrometheusReference* assetReferencesPtr = &_assetReferences[0])
		{
			// PrometheusReference and PrometheusIdentifier have the same memory layout
			UnsafeUtility.MemCpy(identifiers.Ptr, assetReferencesPtr, UnsafeUtility.SizeOf<PrometheusIdentifier>() * _assetReferences.Length);
		}

		new StartUnloadsJob
		{
			identifiers = identifiers,
			prometheus = unmanaged
		}.Run(identifiers.LengthInt);
		
		identifiers.Dispose();
	}

	[BurstCompile]
	public struct StartLoadsJob : IJob
	{
		public UnsafeArray<PrometheusIdentifier> identifiers;
		public PrometheusLoader.Unmanaged prometheus;

		public void Execute()
		{
			for (var i = 0; i < identifiers.Length; i++)
			{
				var identifier = identifiers[i];
				prometheus.StartAssetLoading(identifier, PrometheusLoader.Priority.Normal.Above(3));
			}
		}
	}
    
	[BurstCompile]
	public struct StartUnloadsJob : IJobFor
	{
		public UnsafeArray<PrometheusIdentifier> identifiers;
		public PrometheusLoader.Unmanaged prometheus;

		public void Execute()
		{
			for (var i = 0; i < identifiers.Length; i++)
			{
				var identifier = identifiers[i];
				prometheus.StartAssetLoading(identifier, PrometheusLoader.Priority.Normal.Above(3));
			}
		}
		public void Execute(int index)
		{
			var identifier = identifiers[index];
			prometheus.StartAssetLoading(identifier, PrometheusLoader.Priority.High.Below(12));
		}
	}
}
```

### 4. Multiple Callbacks for the Same Asset

```csharp
using KVD.Prometheus;
using UnityEngine;

public class MultiCallbackUser : MonoBehaviour
{
	[SerializeField, PrometheusReferenceType(typeof(GameObject))]
	private PrometheusReference _assetReference;
	private PrometheusLoader.LoadingTaskHandle _handle;
	private GameObject _instance;

	void OnEnable()
	{
		var callback = PrometheusLoader.CallbackSetup.Delayed(OnAssetLoaded);
		_handle = PrometheusLoader.Instance.LoadAssetAsync(_assetReference, callback, 100);
#if UNITY_EDITOR
		if (_handle.IsValid)
		{
			PrometheusLoader.Instance.AddCallback(_handle, PrometheusLoader.CallbackSetup.Immediate(EDITOR_OnAdditionalCallback));
		}
#endif
	}

	void OnDisable()
	{
		if (_handle.IsValid)
		{
			PrometheusLoader.Instance.UnloadAssetAsync(ref _handle);
			_handle = default;
		}
		if (_instance != null)
		{
			Destroy(_instance);
			_instance = null;
		}
	}

	private void OnAssetLoaded(in PrometheusLoader.LoadingTaskHandle handle)
	{
		var (assetOption, state) = PrometheusLoader.Instance.Result<GameObject>(handle);
		if (state == PrometheusLoader.LoadResultState.Success && assetOption.TryGetValue(out var prefab) && prefab != null)
		{
			_instance = Instantiate(prefab);
		}
	}

#if UNITY_EDITOR
	private void EDITOR_OnAdditionalCallback(in PrometheusLoader.LoadingTaskHandle handle)
	{
		var (assetOption, state) = PrometheusLoader.Instance.Result<GameObject>(handle);
		Debug.Log($"Loading completed with state: {state} and asset: {assetOption}");
	}
#endif
}
```

### 5. Cancelling Loading and Handling Cancellation

```csharp
using KVD.Prometheus;
using UnityEngine;

public class LoadNextAssetUser : MonoBehaviour
{
	[SerializeField, PrometheusReferenceType(typeof(GameObject))]
	private PrometheusReference[] _assetReferences;
	private int _currentIndex = 0;
	private PrometheusLoader.LoadingTaskHandle _handle;
	private GameObject _instance;

	void OnDisable()
	{
		CancelCurrentAssetLoading();

		_currentIndex = 0;
	}

	void Update()
	{
		if (Input.GetKeyDown(KeyCode.Space))
		{
			CancelCurrentAssetLoading();

			_handle = PrometheusLoader.Instance.LoadAssetAsync(_assetReferences[_currentIndex], PrometheusLoader.CallbackSetup.Immediate(OnAssetLoadedOrCancelled));
			_currentIndex = (_currentIndex + 1) % _assetReferences.Length;
		}

		if (Input.GetKeyDown(KeyCode.Escape))
		{
			CancelCurrentAssetLoading();
		}
	}

	private void OnAssetLoadedOrCancelled(in PrometheusLoader.LoadingTaskHandle handle)
	{
		var (assetOption, state) = PrometheusLoader.Instance.Result<GameObject>(handle);
		if (state == PrometheusLoader.LoadResultState.Success && assetOption.TryGetValue(out var prefab))
		{
			_instance = Instantiate(prefab);
		}
	}

	private void CancelCurrentAssetLoading()
	{
		if (_handle.IsValid)
		{
			PrometheusLoader.Instance.TryUnloadAssetAsync(ref _handle);
			_handle = default;
		}
		if (_instance != null)
		{
			Destroy(_instance);
			_instance = null;
		}
	}
}

```
