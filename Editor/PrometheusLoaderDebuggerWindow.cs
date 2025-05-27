using KVD.Utils.Editor;
using UnityEditor;

namespace KVD.Prometheus.Editor
{
	public class PrometheusLoaderDebuggerWindow : EditorWindow
	{
		PrometheusLoaderDebugger _debugger = new PrometheusLoaderDebugger();

		void OnEnable()
		{
			EditorApplication.update -= Repaint;
			EditorApplication.update += Repaint;

			_debugger.Init();
		}

		void OnDisable()
		{
			EditorApplication.update -= Repaint;

			_debugger.Shutdown();
		}

		void OnGUI()
		{
			_debugger.OnGUI();
		}

		[MenuItem(KVDConsts.MenuItemPrefix+"/Prometheus/PrometheusLoader Debugger Window", false, 50)]
		static void ShowWindow()
		{
			var window = GetWindow<PrometheusLoaderDebuggerWindow>();
			window.titleContent = new("Prometheus Debugger Window");
			window.Show();
		}
	}
}
