# Benchmarks

[Table of contents](index.md)

## TO DO

Do in-depth benchmarks against Addressables. Including:

* Memory footprints:
    * Build data size
    * No assets loaded
    * 50% loaded
    * All loaded
    * All loaded multiple times
    * Load => unload
    * Multiple load => unlod
* CPU:
    * Heavy prefab loading
    * Load multiple prefabs at the same time
    * Load heavy binary file (vs Addressables vs File)