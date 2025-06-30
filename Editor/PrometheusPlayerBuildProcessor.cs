using System.IO;
using UnityEditor.Build;

namespace KVD.Prometheus.Editor
{
	public class PrometheusPlayerBuildProcessor : BuildPlayerProcessor
	{
		public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
		{
			var settings = PrometheusSettings.Instance;

			if (settings.buildWithPlayer)
			{
				PrometheusBuilder.BuildPrometheus();
			}

			if (Directory.Exists(PrometheusPersistence.BaseDirectoryPath))
			{
				buildPlayerContext.AddAdditionalPathToStreamingAssets(PrometheusPersistence.BaseDirectoryPath, PrometheusPersistence.MainFolderName);
			}
		}
	}
}
