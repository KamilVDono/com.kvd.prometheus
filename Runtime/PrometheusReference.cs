using System;
using KVD.Utils.DataStructures;
using UnityEngine;

namespace KVD.Prometheus
{
	[Serializable]
	public struct PrometheusReference : IEquatable<PrometheusReference>, IFormattable
	{
		[SerializeField] SerializableGuid assetGuid;
		[SerializeField] long localIdentifier;

		public SerializableGuid AssetGuid => assetGuid;
		public readonly bool IsSet => assetGuid != default && localIdentifier != 0;

		public PrometheusReference(SerializableGuid assetGuid, long localIdentifier)
		{
			this.assetGuid = assetGuid;
			this.localIdentifier = localIdentifier;
		}

		public PrometheusReference(PrometheusIdentifier prometheusIdentifier)
		{
			assetGuid = prometheusIdentifier.assetGuid;
			localIdentifier = prometheusIdentifier.localIdentifier;
		}

		public static implicit operator PrometheusIdentifier(PrometheusReference prometheusReference)
		{
			if (!prometheusReference.IsSet)
			{
				Debug.LogError("PrometheusReference is not set");
			}
			return new(prometheusReference.assetGuid, prometheusReference.localIdentifier);
		}

		public readonly override string ToString()
		{
			return $"PrometheusReference({assetGuid}, {localIdentifier})";
		}

		public readonly string ToString(string format)
		{
			return $"PrometheusReference({assetGuid.ToString(format)}, {localIdentifier})";
		}

		public readonly string ToString(string format, IFormatProvider formatProvider)
		{
			return $"PrometheusReference({assetGuid.ToString(format, formatProvider)}, {localIdentifier})";
		}

		public readonly bool Equals(PrometheusReference other)
		{
			return assetGuid.Equals(other.assetGuid) && localIdentifier == other.localIdentifier;
		}
		public readonly override bool Equals(object obj)
		{
			return obj is PrometheusReference other && Equals(other);
		}
		public readonly override int GetHashCode()
		{
			unchecked
			{
				return (assetGuid.GetHashCode()*397) ^ localIdentifier.GetHashCode();
			}
		}
		public static bool operator ==(PrometheusReference left, PrometheusReference right)
		{
			return left.Equals(right);
		}
		public static bool operator !=(PrometheusReference left, PrometheusReference right)
		{
			return !left.Equals(right);
		}

#if UNITY_EDITOR
		public readonly T EditorAsset<T>() where T : UnityEngine.Object
		{
			var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid.ToString("N"));
			var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath);
			foreach (var asset in assets)
			{
				UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out _, out var localId);
				if (localId == localIdentifier)
				{
					return asset as T;
				}
			}
			return null;
		}

		public readonly UnityEngine.Object EditorMainAsset()
		{
			var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid.ToString("N"));
			return UnityEditor.AssetDatabase.LoadMainAssetAtPath(assetPath);
		}
#endif
	}
}
