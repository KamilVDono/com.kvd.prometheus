# Introduction

[Table of contents](index.md)

## The Name

Prometheus is a hero from Greek mythology. He brought fire, knowledge, and various arts and crafts to humanity.  
Similarly, this package brings assets for your gameplay by efficiently streaming them into your Unity project.

## Purpose

Prometheus allows you to stream-in and stream-out assets during gameplay in a very performant and memory-efficient way. It's designed to replace heavy assets loading systems when you prioritize performance and control over content delivery.

## Motivation

Addressables are very heavy for just simple ref-count-ed load and unload assets. Unloading is very unpredictable, memory footprint is tremendous and everything is managed.
To address these issues, Prometheus was created. By building a custom solution, it was possible to design an API that better suits the needs of performance-critical Unity projects, offering more control and predictability.

## Key features

* **High Performance**: Optimized for low memory footprint and fast loading/unloading with a configurable workload. Prometheus is built to minimize overhead and maximize speed.  
* **Simplicity**: Features a streamlined API for easy use and customization to fit your specific project needs. The API is designed to be easy to understand and hard to misuse.
* **Content Files**: Utilizes Content Files instead of traditional asset bundles as it's newer API.
* **Burst Compatibility**: Supports bursted loading/unloading requests, making it ideal for performance-critical applications.
* **Editor Integration**: Works seamlessly with Unity’s Inspector for assigning asset references. This provides a familiar and intuitive workflow for designers and developers.

## Limitations

* No built-in CDN support. Prometheus is designed for content delivered directly with the game.  
* Packing is fully handled by Unity's build pipeline (maybe there is some way to change [Scriptable Build Pipeline](https://docs.unity3d.com/Packages/com.unity.scriptablebuildpipeline@2.4/manual/index.html) and perform custom packing).  
* No direct replacement for Addressables' Labels feature.

## Is it for your project?

### You want it if…

* You prioritize **performance** and **memory efficiency** in your Unity project.  
* You require **stable and predictable** asset loading and unloading behavior.  
* You want the flexibility to **quickly modify** the asset streaming package to fit unique requirements.  
* Your project benefits from a **burstable API**.  
* Your content is delivered **with the game**, not via an external CDN.

### Don't use it if…

* Your project **requires CDN support** for remote asset delivery and updates.  
* You heavily rely on **update builds** and incremental content delivery through an asset management system.  
* You need a direct, in-place replacement for _all_ Addressables features, especially Labels for asset grouping.