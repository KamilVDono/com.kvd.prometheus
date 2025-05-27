using System.IO;
using UnityEngine;

namespace KVD.Prometheus
{
	public class PrometheusSettings : ScriptableObject
	{
		public bool useBuildData;
		public CompressionType compressionType = CompressionType.LZ4;

		public enum CompressionType : byte
		{
			Uncompressed = 0,
			LZ4 = 1,
			LZMA = 2,
		}

		#region SINGLETON
		static PrometheusSettings _instance;
		public static PrometheusSettings Instance
		{
			get
			{
				if (_instance == null)
				{
					_instance = CreateInstance<PrometheusSettings>();
					var dataPath = Path.Combine(Application.streamingAssetsPath, "PrometheusSettings.asset");
					if (File.Exists(dataPath))
					{
						var json = File.ReadAllText(dataPath);
						JsonUtility.FromJsonOverwrite(json, _instance);
					}
				}
				return _instance;
			}
		}

#if UNITY_EDITOR
		public void OnValidate()
		{
			var dataPath = Path.Combine(Application.streamingAssetsPath, "PrometheusSettings.asset");
			if (!Directory.Exists(Application.streamingAssetsPath))
			{
				Directory.CreateDirectory(Application.streamingAssetsPath);
			}
			var json = JsonUtility.ToJson(this, true);
			File.WriteAllText(dataPath, json);
		}
#endif
		#endregion SINGLETON
	}
}
