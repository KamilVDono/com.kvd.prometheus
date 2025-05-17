using System;
using KVD.Utils.DataStructures;

namespace KVD.Prometheus
{
	public readonly struct PrometheusIdentifier : IEquatable<PrometheusIdentifier>, IFormattable
	{
		public readonly SerializableGuid assetGuid;
		public readonly long localIdentifier;

		public PrometheusIdentifier(SerializableGuid assetGuid, long localIdentifier)
		{
			this.assetGuid = assetGuid;
			this.localIdentifier = localIdentifier;
		}

		public override string ToString()
		{
			return $"PrometheusIdentifier({assetGuid}, {localIdentifier})";
		}

		public string ToString(string format)
		{
			return $"PrometheusIdentifier({assetGuid.ToString(format)}, {localIdentifier})";
		}

		public string ToString(string format, IFormatProvider formatProvider)
		{
			return $"PrometheusIdentifier({assetGuid.ToString(format, formatProvider)}, {localIdentifier})";
		}

		public bool Equals(PrometheusIdentifier other)
		{
			return assetGuid.Equals(other.assetGuid) && localIdentifier == other.localIdentifier;
		}

		public override bool Equals(object obj)
		{
			return obj is PrometheusIdentifier other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				return (assetGuid.GetHashCode()*397) ^ localIdentifier.GetHashCode();
			}
		}

		public static bool operator ==(PrometheusIdentifier left, PrometheusIdentifier right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(PrometheusIdentifier left, PrometheusIdentifier right)
		{
			return !left.Equals(right);
		}
	}
}
