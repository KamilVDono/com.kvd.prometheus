using UnityEditor.Build;

namespace KVD.Prometheus.Editor
{
	public class PrometheusBuildDataMover : BuildPlayerProcessor
	{
		public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
		{
			buildPlayerContext.AddAdditionalPathToStreamingAssets(PrometheusPersistence.BaseDirectoryPath, PrometheusPersistence.MainFolderName);
		}
	}
}
