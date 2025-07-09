#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KVD.Prometheus.Editor
{
	public class PrometheusAssets : ScriptableObject
	{
		public Object[] assets = Array.Empty<Object>();

		HashSet<Object> _assetsSet = new HashSet<Object>();

		[NonSerialized] public ushort bulkMode;

		public static Action onAssetsChanged;

		public static void AddIfNotPreset(Object asset)
		{
			Instance.AddIfNotPresetImpl(asset);
		}

		public static void AddIfNotPreset(HashSet<Object> assetsToAdd)
		{
			Instance.AddIfNotPresetImpl(assetsToAdd);
		}

		public static void Remove(Object asset)
		{
			Instance.RemoveImpl(asset);
		}

		public static bool HasExact(PrometheusReference reference)
		{
			return Instance.HasExactImpl(reference);
		}

		public static bool HasExact(PrometheusIdentifier identifier)
		{
			return Instance.HasExactImpl(identifier);
		}

		public static bool HasExact(Object asset)
		{
			return Instance.HasExactImpl(asset);
		}

		public static bool HasAny(PrometheusReference reference)
		{
			return Instance.HasAnyImpl(reference);
		}

		public static bool HasAny(PrometheusIdentifier identifier)
		{
			return Instance.HasAnyImpl(identifier);
		}

		public static bool HasAny(Object asset)
		{
			return Instance.HasAnyImpl(asset);
		}

		public void AddIfNotPresetImpl(Object asset)
		{
			if (TryAddIfNotPresetNoDirty(asset))
			{
				EditorUtility.SetDirty(this);
				if (bulkMode == 0)
				{
					AssetDatabase.SaveAssetIfDirty(this);
				}
				onAssetsChanged?.Invoke();
			}
		}

		public void AddIfNotPresetImpl(HashSet<Object> assetsToAdd)
		{
			foreach (var asset in assetsToAdd)
			{
				TryAddIfNotPresetNoDirty(asset);
			}
			EditorUtility.SetDirty(this);
			if (bulkMode == 0)
			{
				AssetDatabase.SaveAssetIfDirty(this);
			}
			onAssetsChanged?.Invoke();
		}

		public void RemoveImpl(Object asset)
		{
			var index = Array.IndexOf(assets, asset);
			if (index != -1)
			{
				_assetsSet.Remove(asset);
				Array.Copy(assets, index+1, assets, index, assets.Length-index-1);
				Array.Resize(ref assets, assets.Length-1);
				EditorUtility.SetDirty(this);
				onAssetsChanged?.Invoke();
			}
		}

		public bool HasExactImpl(PrometheusReference reference)
		{
			return HasExactImpl((PrometheusIdentifier)reference);
		}

		public bool HasExactImpl(PrometheusIdentifier identifier)
		{
			var asset = EditorAsset<Object>(identifier);
			return HasExactImpl(asset);
		}

		public bool HasExactImpl(Object asset)
		{
			return asset != null && _assetsSet.Contains(asset);
		}

		public bool HasAnyImpl(PrometheusReference reference)
		{
			return HasAnyImpl((PrometheusIdentifier)reference);
		}

		public bool HasAnyImpl(PrometheusIdentifier identifier)
		{
			var asset = EditorAsset<Object>(identifier);
			return HasAnyImpl(asset);
		}

		public bool HasAnyImpl(Object asset)
		{
			if (HasExactImpl(asset))
			{
				return true;
			}
			var mainAsset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(asset));
			if (asset == null)
			{
				return false;
			}
			foreach (var contentAsset in assets)
			{
				var mainContentAsset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(contentAsset));
				if (mainAsset == mainContentAsset)
				{
					return true;
				}
			}
			return false;
		}

		bool TryAddIfNotPresetNoDirty(Object asset)
		{
			if (asset == null)
			{
				return false;
			}

			if (!_assetsSet.Add(asset))
			{
				return false;
			}

			Array.Resize(ref assets, assets.Length+1);
			assets[^1] = asset;
			return true;
		}

		public T EditorAsset<T>(PrometheusIdentifier identifier) where T : Object
		{
			var assetPath = AssetDatabase.GUIDToAssetPath(identifier.assetGuid.ToString("N"));
			var allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
			foreach (var asset in allAssets)
			{
				AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out _, out var localId);
				if (localId == identifier.localIdentifier)
				{
					return asset as T;
				}
			}
			return null;
		}

		[ContextMenu("Bake")]
		void Bake()
		{
			PrometheusBuilder.BuildPrometheus();
		}

		#region Singleton
		static PrometheusAssets _instance;

		public static PrometheusAssets Instance
		{
			get
			{
				if (_instance == null)
				{
					_instance = LoadOrCreateInstance();
				}
				return _instance;
			}
		}

		static PrometheusAssets LoadOrCreateInstance()
		{
			var directoryPath = "Assets/Prometheus/EditorAssets";
			var assetPath = Path.Combine(directoryPath, $"{nameof(PrometheusAssets)}.asset");

			// Try to load existing asset
			_instance = AssetDatabase.LoadAssetAtPath<PrometheusAssets>(assetPath);

			if (_instance == null)
			{
				// Ensure the Editor folder exists
				if (!Directory.Exists(directoryPath))
				{
					Directory.CreateDirectory(directoryPath);
				}

				// Create new instance
				_instance = CreateInstance<PrometheusAssets>();
				AssetDatabase.CreateAsset(_instance, assetPath);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

#if UNITY_EDITOR
			PrometheusLoader.IsAssetAvailableFunc = HasAny;
#endif

			return _instance;
		}

		[InitializeOnLoadMethod]
		static void InitializeOnLoad()
		{
			_instance ??= LoadOrCreateInstance();

			_instance._assetsSet.Clear();
			_instance._assetsSet.UnionWith(_instance.assets);
		}
		#endregion
	}
}
#endif
