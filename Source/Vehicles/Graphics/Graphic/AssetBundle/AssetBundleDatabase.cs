﻿using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using SmashTools;
using SmashTools.Xml;

namespace Vehicles
{
	[StaticConstructorOnStartup]
	public static class AssetBundleDatabase
	{
		private static readonly Dictionary<string, string> bundleBuildVersionManifest = new Dictionary<string, string>()
		{
			{"1.2", "2019.2.17f1"}
		};

		private static readonly Dictionary<string, Shader> shaderLookup = new Dictionary<string, Shader>();
		private static readonly Dictionary<string, Texture2D> textureLookup = new Dictionary<string, Texture2D>();

		private static readonly string ShaderAssetBundlePath = @"Bundles\RGBShaderBundle";

		private static readonly string CursorAssetBundlePath = @"Bundles\CustomCursor";

		public static readonly AssetBundle ShaderBundle;

		public static readonly AssetBundle CursorBundle;

		public static readonly Shader CutoutComplexRGB;

		public static readonly Shader CutoutComplexPattern;

		public static readonly Texture2D MouseHandOpen;

		public static readonly Texture2D MouseHandClosed;

		static AssetBundleDatabase()
		{
			string version = $"{VersionControl.CurrentMajor}.{VersionControl.CurrentMinor}";
			if (bundleBuildVersionManifest.TryGetValue(version, out string currentVersion))
			{
				if (currentVersion != Application.unityVersion)
				{
					Log.Warning($"{VehicleHarmony.LogLabel} Unity Version {Application.unityVersion} does not match registered version for AssetBundles being loaded. You may encounter problems.");
				}
			}
			string folderChecking = "default";
			List<string> loadFolders = FilePaths.ModFoldersForVersion(VehicleMod.settings.Mod.Content);
			try
			{
				foreach (string folder in loadFolders)
				{
					string versionFilePath = Path.Combine(VehicleMod.settings.Mod.Content.RootDir, folder, ShaderAssetBundlePath);
					folderChecking = versionFilePath;
					if (File.Exists(versionFilePath))
					{
						ShaderBundle = AssetBundle.LoadFromFile(versionFilePath);
						if (ShaderBundle is null) throw new NullReferenceException();

						CutoutComplexRGB = LoadAssetBundleShader("Assets/Shaders/ShaderRGB.shader");
						CutoutComplexPattern = LoadAssetBundleShader("Assets/Shaders/ShaderRGBPattern.shader");
					}
					goto CursorLoading;
				}
				throw new IOException();
			}
			catch (Exception ex)
			{
				SmashLog.Error($"Unable to load AssetBundle at <text>{folderChecking}</text>\nException = {ex.Message}");
			}
			CursorLoading:;
			try
			{
				foreach (string folder in loadFolders)
				{
					string versionFilePath = Path.Combine(VehicleMod.settings.Mod.Content.RootDir, folder, CursorAssetBundlePath);
					folderChecking = versionFilePath;
					if (File.Exists(versionFilePath))
					{
						CursorBundle = AssetBundle.LoadFromFile(versionFilePath);
						if (CursorBundle is null) throw new NullReferenceException();

						MouseHandOpen = LoadAssetBundleTexture("Assets/Textures/MouseHandOpen.png");
						MouseHandClosed = LoadAssetBundleTexture("Assets/Textures/MouseHandClosed.png");
					}
				}
			}
			catch (Exception ex)
			{
				SmashLog.Error($"Unable to load AssetBundle at <text>{CursorAssetBundlePath}</text>\nException = {ex.Message}");
			}
		}

		public static Shader LoadAssetBundleShader(string path)
		{
			if (shaderLookup.TryGetValue(path, out Shader shader))
			{
				return shader;
			}
			return (Shader)ShaderBundle.LoadAsset(path);
		}

		public static Texture2D LoadAssetBundleTexture(string path)
		{
			if (textureLookup.TryGetValue(path, out Texture2D texture))
			{
				return texture;
			}
			return (Texture2D)CursorBundle.LoadAsset(path);
		}

		public static bool SupportsRGBMaskTex(this Shader shader)
		{
			return shader == CutoutComplexPattern;
		}
	}
}