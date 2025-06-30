# Prometheus: Asset Streaming for Unity

## Description

Prometheus is an open-source Unity package for efficient content streaming (loading and unloading assets).
It’s optimized for performance, designed for content delivered with the game (no CDN support).
Prometheus replaces Addressables, but it's not an in-place replacement as it has a different API in order to minimize overhead.
Prometheus supports **bursted loading/unloading** requests. Loaded asset is a managed type so cannot be bursted (maybe there will be a hack with similar to `UnityObjectRef` entity).

## Quick installation

> **Important**: Follow the order below, as Prometheus depends on the `com.kvd.utils` package. Incorrect order may cause errors.

1. Add the `com.kvd.utils` package (a utility library used by Prometheus) to your Unity project via the Package Manager:
   - See [Unity’s guide for Git packages](https://docs.unity3d.com/Manual/upm-ui-giturl.html).
   - URL: `https://github.com/KamilVDono/com.kvd.utils.git`
2. Add the Prometheus package the same way:
   - URL: `https://github.com/KamilVDono/com.kvd.prometheus.git`

For detailed installation options, see [Setup Documentation](Documentation~/setup.md).

## Quick start

Get started with Prometheus in a few steps:

1. Install the package as described above.
2. Add a serialized field to your MonoBehaviour:
   ```csharp
   public PrometheusReference assetReference;
   ```
   - Use `[PrometheusReferenceType(typeof(TAssetType))]` to constrain draggable asset types in the Inspector.
3. Start loading an asset:
   ```csharp
   PrometheusLoader.Instance.StartAssetLoading(assetReference);
   ```
4. Retrieve the loaded asset:
   ```csharp
   var result = PrometheusLoader.Instance.GetAsset<TAssetType>(assetReference);
   if (result.TryGetValue(out var asset))
   {
       // Asset is fully loaded and ready to use
   }
   else
   {
       // Asset is still loading or failed to load
   }
   ```
   - `Option<TAssetType>` returns:
     - `None`: Asset not available (still loading or failed).
     - `Some`: Asset fully loaded as `TAssetType`.
5. Unload the asset when done:
   ```csharp
   PrometheusLoader.Instance.StartAssetUnloading(assetReference);
   ```

There are more queries, bursted API, callbacks API and more, see [Usage documentation](Documentation~/usage.md).

### Example

```csharp
using Prometheus;
using UnityEngine;

public class AssetLoaderExample : MonoBehaviour
{
    [PrometheusReferenceType(typeof(GameObject))]
    public PrometheusReference prefabReference;

    GameObject _instance;

    void Start()
    {
        PrometheusLoader.Instance.StartAssetLoading(prefabReference);
    }

    void Update()
    {
        if (_instance)
        {
            return;
        }
        
        var result = PrometheusLoader.Instance.GetAsset<GameObject>(assetReference);
        if (result.TryGetValue(out var prefab))
        {
            Debug.Log($"Prefab loaded: {prefab.name}");
            _instance = Instantiate(prefab);
        }
    }

    void OnDestroy()
    {
        if (_instance)
        {
            Destroy(_instance);
        }
        PrometheusLoader.Instance.StartAssetUnloading(prefabReference);
    }
}
```

## Documentation

Explore detailed guides and API references:
- [Documentation Overview](Documentation~/index.md)
- [Introduction](Documentation~/introduction.md)
- [Setup Guide](Documentation~/setup.md)
- [Usage Guide](Documentation/usage.md)

## License

Prometheus is licensed under the MIT License. See [LICENSE](LICENSE.md) for details.

## Future Plans

- Further allocations minimization
- Explore lightweight asset references (e.g., inspired by `UnityObjectRef`) for bursted contexts.
- Parallel burst API
- Optimized tooling for massive projects

