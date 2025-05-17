namespace KVD.Prometheus
{
	public partial class PrometheusLoader
	{
		public static PrometheusLoader Instance{ get; private set; }

#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
#else
		[UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
		static void Initialize()
		{
			Instance = new();
		}
	}
}
