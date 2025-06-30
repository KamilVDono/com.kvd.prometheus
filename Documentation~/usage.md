# Usage

[Table of contents](index.md)

**Main API is accessible via PrometheusLoader.Instance**

**For priority:** Higher value mean higher priority, higher priority mean it will be streaming/processed at first, _**don't mean it will be loaded first**_. If you start loading of lightweight mesh with priority 1 and heavy prefab with priority 200, then there is high probability that mesh will finish loading first.

## Common API

### Data structures

`Option<T>` - Value `T` or None, similar to nullable, forces user to deal with possibility of None 

`PrometheusIdentifier` - Identifier of asset, is immutable and preferred for runtime usage

`PrometheusReference` - Soft reference to asset, similar to `PrometheusIdentifier` but mutable with possibility to setup from editor

### Streaming

### Methods

`void StartAssetLoading(identifier, priority)` - Increments references count of asset with `identifier` and starts loading if previously reference count was 0

`void StartAssetUnloading(identifier, priority)` - Decrement references count of asset with `identifier`and starts unloading if now reference count is equal to 0

`Option<T> GetAsset<T>(identifier) where T : Object` - If asset related to identifier successfully loaded then returns asset, otherwise None

`Option<T> ForceGetAsset<T>(identifier) where T : Object` - If asset is not loaded then synchronously completes loading, then operates like `GetAsset<T>`

### Queries

### Methods

`bool IsActive(in identifier)` - true if reference count is greater than 0, otherwise false. In other words, true if you called StartAssetLoading more times than StartAssetUnloading or it is dependency of other Active asset.

`bool IsLoading(in identifier)` - true if asset is at any loading state, false if loaded or reference count equals 0.

`bool IsLoaded(in identifier)` - true if asset is at any loaded state (successful or failed), otherwise false.

`bool IsSuccessfullyLoaded(in identifier)` - true if asset is loaded and loading yield success

## Bursted API

By calling `PrometheusLoader.Instance.Unmanaged` you get reference to unmanaged struct that provides API for bursted operations. You can pass it as pointer to Job.

### Methods

`void StartAssetLoading(identifier, priority)` - Increments references count of asset with `identifier` and starts loading if previously reference count was 0

`void StartAssetUnloading(identifier, priority)` - Decrement references count of asset with `identifier`and starts unloading if now reference count is equal to 0

## Callbacks

**This may be looking like simpler way, but in real callbacks are more complex when cancellation is in game.**

### Data structures

`CallbackSetup` - struct which holds callback setup, you can create two types:
- Immediate - will be called Immediate inside `LoadAssetAsync` if asset is already loaded
- Delayed - even if asset is already loaded, callback will be called during next `PreUpdate`(don't mean next frame, if you call loading during any point of player loop before `PreUpdate`, then it will be called the same frame)

`LoadingTaskHandle` - represents asset loading, allows to unload/cancel loading and query for current state of loading. Handles has built-in safety checks, so it shouldn't be possible to invalidate state of loading.

`LoadResultState` - enumeration for current state of loading. Values are:
- **Invalid** - `LoadingTaskHandle` was invalid
- **Fail** - Loading is done but asset is not available (maybe corrupted or missing asset file)
- **Cancelled** - Loading was cancelled
- **Success** - Loading done with success
- **InProgress** - Loading still ongoing

### Methods

`LoadingTaskHandle LoadAssetAsync(in identifier, in callbackSetup, priority)` - starts asset loading, if asset is available for streaming (is in build) then returns valid `LoadingTaskHandle`, otherwise `LoadingTaskHandle` will be invalid.

`void UnloadAssetAsync(ref handle)` - Unload (or cancel loading if still loading) asset. **Error will be printed if handle is not in `Fail`, `Success` or `InProgress` state.**

`bool TryUnloadAssetAsync(ref handle)` - Unload (or cancel loading if still loading) asset, return true if unloading succeed, if handle was already cancelled or invalid then false. **Error will be printed if handle is `Invalid`.**

`(Option<T>, LoadResultState) Result<T>(in handle) where T : Object` - Check state of loading, returns pair with option to asset (same as `GetAsset`) and loading state. You probably will call it from callback.

`void AddCallback(in handle, in callbackSetup)` - Allows you to append additional callback to existing loading. _If loading is cancelled then callback will be always immediate._

### Cancellation

After calling `UnloadAssetAsync` you will still get callback, were `LoadResultState` will be `Cancelled`. 

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
