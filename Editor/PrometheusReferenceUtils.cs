using KVD.Utils.DataStructures;
using UnityEditor;
using UnityEngine;

namespace KVD.Prometheus.Editor
{
	public static class PrometheusReferenceUtils
	{
		public static PrometheusReference GetFromAsset(Object asset)
		{
			var assetPath = AssetDatabase.GetAssetPath(asset);

			// Default Unity assets are typically empty or contain "Library/unity default resources"
			if (string.IsNullOrEmpty(assetPath) || assetPath.StartsWith("Library"))
			{
				return default;
			}

			var newGuid = default(SerializableGuid);
			var newLocalIdentifier = 0L;
			if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var newAssetGuid, out var newLocalId))
			{
				newGuid = new(newAssetGuid);
				newLocalIdentifier = newLocalId;
				PrometheusAssets.AddIfNotPreset(asset);
			}
			return new(newGuid, newLocalIdentifier);
		}

		public static PrometheusReference GetFromAssetNoAdd(Object asset)
		{
			var assetPath = AssetDatabase.GetAssetPath(asset);

			// Default Unity assets are typically empty or contain "Library/unity default resources"
			if (string.IsNullOrEmpty(assetPath) || assetPath.StartsWith("Library"))
			{
				return default;
			}

			var newGuid = default(SerializableGuid);
			var newLocalIdentifier = 0L;
			if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var newAssetGuid, out var newLocalId))
			{
				newGuid = new(newAssetGuid);
				newLocalIdentifier = newLocalId;
			}
			return new(newGuid, newLocalIdentifier);
		}

		public static void SaveIfDirty()
		{
			if (EditorUtility.IsDirty(PrometheusAssets.Instance))
			{
				AssetDatabase.SaveAssetIfDirty(PrometheusAssets.Instance);
			}
		}

		public static void EnterBulkMode()
		{
			++PrometheusAssets.Instance.bulkMode;
		}

		public static void ExitBulkMode()
		{
			--PrometheusAssets.Instance.bulkMode;
			if (PrometheusAssets.Instance.bulkMode == 0)
			{
				SaveIfDirty();
			}
		}
	}
}
