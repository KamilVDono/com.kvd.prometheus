using System.IO;
#if !UNITY_EDITOR
using UnityEngine;
#endif

namespace KVD.Prometheus
{
	public static class PrometheusPersistence
	{
		public const string MainFolderName = "Prometheus";
		const string ArchivesFolderName = "Archives";
		const string MappingsFileName = "PrometheusData.bin";

		static string StartingDirectory =>
#if UNITY_EDITOR
			"Library"
#else
			Application.streamingAssetsPath
#endif
		;

		public static string BaseDirectoryPath => Path.Combine(StartingDirectory, MainFolderName);
		public static string ArchivesDirectoryPath => Path.Combine(BaseDirectoryPath, ArchivesFolderName);
		public static string MappingsFilePath => Path.Combine(BaseDirectoryPath, MappingsFileName);
	}
}
