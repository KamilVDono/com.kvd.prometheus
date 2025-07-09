# Tooling

[Table of contents](index.md)

All windows and actions are available through `MenuItems` under `KVD/Prometheus`. You can change the location of menu items by modifying the `KVDConsts.MenuItemPrefix` constant.
![prometheus_menu_items.png](./images/prometheus_menu_items.png)

## Add assets to Prometheus

Assets have a Prometheus header that lets you inspect and change their current state. The header code is in `IsInPrometheusHeader`.
Possible states:

* Direct referenced - there is a direct reference to that asset
  ![directly_referenced.png](./images/directly_referenced.png)
* Indirect reference - at least one of the asset's hierarchy is a direct reference
  ![indirect_referenced.png](./images/indirect_referenced.png)
* Not referenced
  ![not_referenced.png](./images/not_referenced.png)

### Indirect reference

Some assets are simple and consist of just a single element in their asset hierarchy. Others, however, have complex hierarchies with many assets inside. For example, importing a model can create a model prefab, LOD meshes, default materials, and possibly textures.

Let's take a simple example: you import an FBX file with LOD0, LOD1, LOD2, and a collision mesh. You have a rendering system that unloads unused LODs and a system that streams colliders at a fixed distance. The setup would be:

* LOD0 - [0, 22)
* LOD1 - [20, 155)
* LOD2 - [150, 510)
* Collider - [0, 1200)

This seems like a good way to reduce memory usage, but since all these assets come from a single asset hierarchy, all of them will be loaded whenever any one of them is loaded. That means loading the collider mesh will also load all the LOD meshes. This is true not only for Content Files, but for all Unity systems (as far as I know). Therefore, you may want to author assets so that each asset hierarchy contains only a single element.

## Settings

Available under `KVD/Prometheus/Settings`.

![settings_window.png](./images/settings_window.png)

* Use Build Data - Determines whether to use build data or fall back to the AssetDatabase.
  * `True` - Build data will be used.
  * `False` - AssetDatabase will be used.
* Build With Player - Prometheus automatically builds when you build your game.
* Compression Type - Which compression should be used for Content Files. Check [Unity documentation](https://docs.unity3d.com/ScriptReference/CompressionType.html).

## Build

Available under `KVD/Prometheus/Build Prometheus`.
This performs only the Prometheus build, meaning it builds Prometheus metadata and Content Files. The player will not be built.

Build data can be found in `Library/Prometheus`. At player build, it will be copied to the streaming assets.

## Debug

### Prometheus loader debugger

Available under `KVD/Prometheus/PrometheusLoader Debugger Window`.

`PrometheusLoaderDebugger` is runtime compatible, so you can use it at runtime and debug in builds.

![debugger_window.png](./images/debugger_window.png)

* **Integration time slice**: how much time Content files can take on the main thread to integrate assets

#### Mapping

![mapping_debugger.png](./images/mapping_debugger.png)

* **Registered content files**: Count of content files (not physical file count, but how many files were created and are needed by the system to fully function)
* **Registered assets**: Assets which can be requested
  * **Id**: Asset identifier
  * **Content file**: Content file identifier
  * **State**: Current state of the asset
  * **(Un)load**: Button which allows you to force load or unload an asset
  * **Asset**: Editor source of the baked asset
  * **Editor Select**: Allows you to select the editor source asset

#### File management

![file_management_debugger.png](./images/file_management_debugger.png)

* **File Management Paused**: Can pause asset streaming
* **Ongoings**: Allows you to check how many assets are in each state of streaming
  * **Mounting**: Count of `Archives` currently mounting
  * **Content Loading**: Count of `Content Files` currently loading
  * **Unmounting**: Count of `Archives` currently unmounting
  * **Content Unloading**: Count of `Content Files` currently unloading
  * **For mounting**: Count of `Archives` requested to start mounting
  * **For dependencies**: Count of `Content Files` waiting for dependencies to be loaded first; only when all dependencies are loaded does `Content File` loading start
  * **For unloading**: Count of `Content Files` waiting to start unloading
  * **For unmounting**: Count of `Archives` waiting to start unmounting
* **Loaded**: Count of requested assets (directly or as dependencies); when expanded, shows more information about each request
  * **ID**: Loading index
  * **Content files**: `Content File` GUID (archive which contains it has the same GUID)
  * **State**: State of the `Content File`
  * **Ref count**: Reference counter for this `Content File`
  * **Inspect**: Allows you to inspect the content of the `Content File`

![debugger_inspect.png](./images/debugger_inspect.png)

#### Callbacks

![debugger_callbacks.png](./images/debugger_callbacks.png)

* **Registered loading tasks**: How many tasks are registered (loading or loaded but not released yet)
* **Loading tasks with callbacks**: How many tasks have a waiting callback

## Archive explorer

![archive_explorer.png](./images/archive_explorer.png)

![archive_expanded.png](./images/archive_expanded.png)

Allows you to check the content of an archive, its dependencies, and dependents.

To use, select an archive file and click the _Load_ button. This will load the archive along with all its dependencies.

After you expand the archive, there will be:

* Size of archive (an archive can contain two files, similar to [AssetBundle](https://docs.unity3d.com/6000.1/Documentation/Manual/AssetBundlesIntro.html): one with serialized files and one with resource files)
* Dependencies foldout - allows you to load/inspect dependencies
* Dependents foldout - allows you to check which other archive needs the current one; from here you can load it
* Content of archive - allows you to inspect the content of the archive

## Asset data info

![asset_data_info.png](./images/asset_data_info.png)

Allows you to find the GUID and local identifier of the asset. If not locked, these values will change when you select a different asset.
