﻿using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using RimWorld;
using SmashTools;

namespace Vehicles
{
	public class StatUpgrade : Upgrade
	{
		public List<StatDefUpgrade> stats;

		public List<VehicleStatDefUpgrade> vehicleStats;

		public List<StatCategoryUpgrade> statCategories;

		public override bool UnlockOnLoad => true;

		public override void Unlock(VehiclePawn vehicle, bool unlockingAfterLoad)
		{
			if (!stats.NullOrEmpty())
			{
				foreach (StatDefUpgrade statDefUpgrade in stats)
				{
					switch (statDefUpgrade.type)
					{
						case UpgradeType.Add:
							vehicle.statHandler.AddUpgradeableStatValue(statDefUpgrade.def, statDefUpgrade.value);
							break;
						case UpgradeType.Set:
							vehicle.statHandler.SetUpgradeableStatValue(node.key, statDefUpgrade.def, statDefUpgrade.value);
							break;
					}
				}
			}
			if (!vehicleStats.NullOrEmpty())
			{
				foreach (VehicleStatDefUpgrade vehicleStatDefUpgrade in vehicleStats)
				{
					switch (vehicleStatDefUpgrade.type)
					{
						case UpgradeType.Add:
							vehicle.statHandler.AddStatOffset(vehicleStatDefUpgrade.def, vehicleStatDefUpgrade.value);
							break;
						case UpgradeType.Set:
							vehicle.statHandler.SetStatOffset(node.key, vehicleStatDefUpgrade.def, vehicleStatDefUpgrade.value);
							break;
					}
				}
			}
			if (!statCategories.NullOrEmpty())
			{
				foreach (StatCategoryUpgrade statCategoryUpgrade in statCategories)
				{
					switch (statCategoryUpgrade.type)
					{
						case UpgradeType.Add:
							vehicle.statHandler.AddStatOffset(statCategoryUpgrade.def, statCategoryUpgrade.value);
							break;
						case UpgradeType.Set:
							vehicle.statHandler.SetStatOffset(node.key, statCategoryUpgrade.def, statCategoryUpgrade.value);
							break;
					}
				}
			}
		}

		public override void Refund(VehiclePawn vehicle)
		{
			if (!stats.NullOrEmpty())
			{
				foreach (StatDefUpgrade statDefUpgrade in stats)
				{
					switch (statDefUpgrade.type)
					{
						case UpgradeType.Add:
							vehicle.statHandler.SubtractUpgradeableStatValue(statDefUpgrade.def, statDefUpgrade.value);
							break;
						case UpgradeType.Set:
							vehicle.statHandler.RemoveUpgradeableStatValue(node.key, statDefUpgrade.def);
							break;
					}
				}
			}
			if (!vehicleStats.NullOrEmpty())
			{
				foreach (VehicleStatDefUpgrade vehicleStatDefUpgrade in vehicleStats)
				{
					switch (vehicleStatDefUpgrade.type)
					{
						case UpgradeType.Add:
							vehicle.statHandler.SubtractStatOffset(vehicleStatDefUpgrade.def, vehicleStatDefUpgrade.value);
							break;
						case UpgradeType.Set:
							vehicle.statHandler.RemoveStatOffset(node.key, vehicleStatDefUpgrade.def);
							break;
					}
				}
			}
			if (!statCategories.NullOrEmpty())
			{
				foreach (StatCategoryUpgrade statCategoryUpgrade in statCategories)
				{
					switch (statCategoryUpgrade.type)
					{
						case UpgradeType.Add:
							vehicle.statHandler.SubtractStatOffset(statCategoryUpgrade.def, statCategoryUpgrade.value);
							break;
						case UpgradeType.Set:
							vehicle.statHandler.RemoveStatOffset(node.key, statCategoryUpgrade.def);
							break;
					}
				}
			}
		}

		public struct StatDefUpgrade
		{
			public StatDef def;
			public float value;

			public UpgradeType type;
		}

		public struct VehicleStatDefUpgrade
		{
			public VehicleStatDef def;
			public float value;

			public UpgradeType type;
		}

		public struct StatCategoryUpgrade
		{
			public StatUpgradeCategoryDef def;
			public float value;

			public UpgradeType type;
		}
	}
}
