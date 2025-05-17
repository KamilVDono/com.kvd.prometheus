using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace KVD.Prometheus
{
	[Conditional("UNITY_EDITOR")]
	public class PrometheusReferenceTypeAttribute : PropertyAttribute
	{
		public Type Type{ get; }

		public PrometheusReferenceTypeAttribute(Type type)
		{
			if (!typeof(Object).IsAssignableFrom(type))
			{
				Debug.LogError($"Type {type} is not an asset type");
			}
			Type = type;
		}
	}
}
