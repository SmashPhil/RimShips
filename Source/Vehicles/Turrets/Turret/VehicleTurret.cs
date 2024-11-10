﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;
using SmashTools;
using SmashTools.Performance;
using Verse.AI;

namespace Vehicles
{
	public class VehicleTurret : IExposable, ILoadReferenceable, IEventManager<VehicleTurretEventDef>, IMaterialCacheTarget, ITweakFields
	{
		public const int AutoTargetInterval = 60;
		public const int TicksPerOverheatingFrame = 15;
		public const int TicksTillBeginCooldown = 60;
		public const float MaxHeatCapacity = 100;
		public const int DefaultMaxRange = 9999;

		//WIP - may be removed in the future
		public static HashSet<Pair<string, TurretDisableType>> conditionalTurrets = new HashSet<Pair<string, TurretDisableType>>();

		/* --- Parsed --- */

		public int uniqueID = -1;
		public string parentKey;
		public string key;
		public string groupKey;

		[Unsaved]
		public VehicleTurret reference;
		[TweakField]
		public VehicleTurretDef turretDef;

		public TargetLock targeting = TargetLock.Pawn | TargetLock.Thing;
		[TweakField(SettingsType = UISettingsType.Checkbox)]
		public bool targetPersists = false;
		[TweakField(SettingsType = UISettingsType.Checkbox)]
		public bool autoTargeting = true;
		[TweakField(SettingsType = UISettingsType.Checkbox)]
		public bool manualTargeting = true;

		[TweakField(SettingsType = UISettingsType.SliderEnum)]
		public DeploymentType deployment = DeploymentType.None;

		[TweakField]
		public VehicleTurretRender renderProperties = new VehicleTurretRender();

		public TurretComponentRequirement component;

		[TweakField(SettingsType = UISettingsType.FloatBox)]
		public Vector2 aimPieOffset = Vector2.zero;
		[TweakField(SettingsType = UISettingsType.FloatBox)]
		public Vector2 angleRestricted = Vector2.zero;
		[TweakField(SettingsType = UISettingsType.IntegerBox)]
		public int drawLayer = 1;
		
		public float defaultAngleRotated = 0f;
		public string gizmoLabel;

		/* ----------------- */

		public string upgradeKey;

		//TODO 1.6 - rename
		public LocalTargetInfo cannonTarget;

		protected float rotation = 0; //True rotation of turret separate from Vehicle rotation and angle for saving
		protected float restrictedTheta;

		public ThingDef loadedAmmo;
		public ThingDef savedAmmoType;

		public int shellCount;

		protected bool autoTargetingActive;

		private int reloadTicks;
		private int burstTicks;

		protected int currentFireMode;
		public float currentHeatRate;
		protected bool triggeredCooldown;
		protected int ticksSinceLastShot;
		public bool queuedToFire = false;

		protected Rot4 parentRotCached = default;
		protected float parentAngleCached = 0f;

		protected int burstsTillWarmup;

		[Unsaved]
		protected float rotationTargeted = 0f;

		[Unsaved]
		public VehiclePawn vehicle;
		[Unsaved]
		public VehicleDef vehicleDef; ///Necessary to separate from vehicle for def-contained turrets, ie. <see cref="CompProperties_VehicleTurrets"/> for PatternData
		[Unsaved]
		public VehicleTurret attachedTo;
		[Unsaved]
		public List<VehicleTurret> childTurrets = new List<VehicleTurret>();
		[Unsaved]
		protected List<VehicleTurret> groupTurrets;
		[Unsaved]
		public TurretRestrictions restrictions;

		//TODO 1.6 - merge recoil trackers
		[Unsaved]
		public Turret_RecoilTracker recoilTracker;
		[Unsaved]
		public Turret_RecoilTracker[] recoilTrackers;

		//Cache all root draw pos on spawn
		[Unsaved]
		protected Vector3 rootDrawPos_North;
		[Unsaved]
		protected Vector3 rootDrawPos_East;
		[Unsaved]
		protected Vector3 rootDrawPos_South;
		[Unsaved]
		protected Vector3 rootDrawPos_West;
		[Unsaved]
		protected Vector3 rootDrawPos_NorthEast;
		[Unsaved]
		protected Vector3 rootDrawPos_SouthEast;
		[Unsaved]
		protected Vector3 rootDrawPos_SouthWest;
		[Unsaved]
		protected Vector3 rootDrawPos_NorthWest;

		public Texture2D currentFireIcon;
		protected Texture2D gizmoIcon;
		protected Texture2D mainMaskTex;

		protected Texture2D cachedTexture;
		protected Material cachedMaterial;
		protected Graphic_Turret cachedGraphic;
		protected GraphicDataRGB cachedGraphicData;

		protected List<TurretDrawData> turretGraphics;

		protected RotatingList<Texture2D> overheatIcons;

		protected MaterialPropertyBlock materialPropertyBlock;

		private static readonly List<(Thing, int)> thingsToTakeReloading = new List<(Thing, int)>();

		/* --------- CE hooks for compatibility --------- */
		/// <summary>
		/// (projectileDef, ammoDef, AmmoSetDef, origin, intendedTarget, launcher, shotAngle, shotRotation, shotHeight, shotSpeed, ret CE projectile)
		/// </summary>
		public static Func<ThingDef, ThingDef, Def, Vector2, LocalTargetInfo, VehiclePawn, float, float, float, float, object> LaunchProjectileCE = null;

		/// <summary>
		/// (velocity, range, shooter, target, origin, flyOverhead, gravityModifier, sway, spread, recoil, ret 2-angles)
		/// </summary>
		public static Func<float, float, Thing, LocalTargetInfo, Vector3, bool, float, float, float, float, Vector2> ProjectileAngleCE = null;

		/// <summary>
		/// (ammoset name, ret ammoset def)
		/// </summary>
		public static Func<string, Def> LookupAmmosetCE = null;

		/// <summary>
		/// (projectileDef, ammoDef, ammosetDef, turret, recoilAmount)
		/// </summary>
		public static Action<ThingDef, ThingDef, Def, VehicleTurret, float> NotifyShotFiredCE = null;


	        /// <summary>
		/// (ammoDef, ammosetDef, spread, ret Tuple<projectileCount, spread>)
		/// </summary>
		public static Func<ThingDef, Def, float, Tuple<int, float>> LookupProjectileCountAndSpreadCE = null;
		/* ---------------------------------------------- */

		/// <summary>
		/// Init from CompProperties
		/// </summary>
		public VehicleTurret()
		{
		}

		/// <summary>
		/// Init from save file
		/// </summary>
		public VehicleTurret(VehiclePawn vehicle)
		{
			this.vehicle = vehicle;
			vehicleDef = vehicle.VehicleDef;
		}

		/// <summary>
		/// Newly Spawned
		/// </summary>
		/// <param name="vehicle"></param>
		/// <param name="reference">VehicleTurret as defined in xml</param>
		public VehicleTurret(VehiclePawn vehicle, VehicleTurret reference)
		{
			this.vehicle = vehicle;
			vehicleDef = vehicle.VehicleDef;

			uniqueID = Find.UniqueIDsManager.GetNextThingID();
			turretDef = reference.turretDef;

			gizmoLabel = reference.gizmoLabel;

			key = reference.key;

			targetPersists = reference.turretDef.turretType == TurretType.Static ? false : reference.targetPersists;
			autoTargeting = reference.turretDef.turretType == TurretType.Static ? false : reference.autoTargeting;
			manualTargeting = reference.turretDef.turretType == TurretType.Static ? false : reference.manualTargeting;

			currentFireMode = 0;
			currentFireIcon = OverheatIcons.FirstOrDefault();
			ticksSinceLastShot = 0;
			burstsTillWarmup = 0;

			TurretRotation = reference.defaultAngleRotated;

			ResolveCannonGraphics(vehicle);
			InitRecoilTrackers();
		}

		string ITweakFields.Label => nameof(VehicleTurret);////turretDef.LabelCap;

		string ITweakFields.Category => turretDef.LabelCap;

		public bool GizmoHighlighted { get; set; }

		public bool TargetLocked { get; private set; }

		public int PrefireTickCount { get; private set; }

		public int CurrentTurretFiring { get; set; }

		public bool IsManned { get; private set; }

		public PawnStatusOnTarget CachedPawnTargetStatus { get; set; }

		public bool IsTargetable => turretDef?.turretType == TurretType.Rotatable;

		public bool RotationAligned => rotation == rotationTargeted;

		public bool TurretRestricted => restrictions?.Disabled ?? false;

		public virtual bool TurretDisabled => TurretRestricted || !IsManned || !DeploymentSatisfied || ComponentDisabled;

		public bool ComponentDisabled => component != null && !component.MeetsRequirements;

		protected virtual bool TurretTargetValid => cannonTarget.Cell.IsValid && !TurretDisabled;

		public bool NoGraphic => turretDef.graphicData is null;

		public bool CanAutoTarget => autoTargeting || VehicleMod.settings.debug.debugShootAnyTurret;

		public int WarmupTicks => Mathf.CeilToInt(turretDef.warmUpTimer * 60f);

		public bool OnCooldown => triggeredCooldown;

		public bool CanOverheat => VehicleMod.settings.main.overheatMechanics && turretDef.cooldown != null && turretDef.cooldown.heatPerShot > 0;

		public bool HasAmmo => turretDef.ammunition is null || shellCount > 0;

		public bool ReadyToFire => groupKey.NullOrEmpty() ? (burstTicks <= 0 && ReloadTicks <= 0 && !TurretDisabled) : GroupTurrets.Any(t => t.burstTicks <= 0 && t.ReloadTicks <= 0 && !t.TurretDisabled);

		public bool FullAuto => CurrentFireMode.ticksBetweenBursts.TrueMin == CurrentFireMode.ticksBetweenShots;

		public int ReloadTicks => reloadTicks;

		public float DrawLayerOffset => drawLayer * (Altitudes.AltInc / GraphicDataLayered.SubLayerCount) + VehicleRenderer.YOffset_Body;

		public EventManager<VehicleTurretEventDef> EventRegistry { get; set; }

		public bool DeploymentSatisfied
		{
			get
			{
				return deployment switch
				{
					DeploymentType.None => true,
					DeploymentType.Deployed => vehicle.CompVehicleTurrets.Deployed && !vehicle.Deploying,
					DeploymentType.Undeployed => !vehicle.CompVehicleTurrets.Deployed && !vehicle.Deploying,
					_ => throw new NotImplementedException(nameof(DeploymentType))
				};
			}
		}

		public string DeploymentDisabledReason
		{
			get
			{
				return deployment switch
				{
					DeploymentType.None => string.Empty,
					DeploymentType.Deployed => "VF_MustBeDeployed".Translate(),
					DeploymentType.Undeployed => "VF_MustBeUndeployed".Translate(),
					_ => throw new NotImplementedException(nameof(DeploymentType))
				};
			}
		}

		public int MaxTicks
		{
			get
			{
				float maxTicks = turretDef.reloadTimer * 60f;
				if (turretDef.reloadTimerMultiplierPerCrewCount != null)
				{
					List<Pawn> gunners = vehicle.AllCannonCrew;
					maxTicks *= turretDef.reloadTimerMultiplierPerCrewCount.Evaluate(gunners.Count);
				}
				return Mathf.CeilToInt(maxTicks);
			}
		}

		public ThingDef ProjectileDef
		{
			get
			{
				if (loadedAmmo != null && loadedAmmo.projectileWhenLoaded != null)
				{
					return loadedAmmo.projectileWhenLoaded;
				}
				return turretDef.projectile;
			}
		}

		public Texture2D FireIcon
		{
			get
			{
				if (Find.TickManager.TicksGame % TicksPerOverheatingFrame == 0)
				{
					currentFireIcon = OverheatIcons.Next;
				}
				return currentFireIcon;
			}
		}

		protected RotatingList<Texture2D> OverheatIcons
		{
			get
			{
				if (overheatIcons.NullOrEmpty())
				{
					overheatIcons = VehicleTex.FireIcons.ToRotatingList();
				}
				return overheatIcons;
			}
		}

		public MaterialPropertyBlock MatPropertyBlock
		{
			get
			{
				if (materialPropertyBlock is null)
				{
					materialPropertyBlock = new MaterialPropertyBlock();
				}
				return materialPropertyBlock;
			}
			set
			{
				materialPropertyBlock = value;
			}
		}

		public List<VehicleTurret> GroupTurrets
		{
			get
			{
				if (groupTurrets is null)
				{
					if (groupKey.NullOrEmpty())
					{
						groupTurrets = new List<VehicleTurret>() { this };
					}
					else
					{
						groupTurrets = vehicle.CompVehicleTurrets.turrets.Where(t => t.groupKey == groupKey).ToList();
					}
				}
				return groupTurrets;
			}
		}

		public virtual int MaxShotsCurrentFireMode
		{
			get
			{
				if (FullAuto)
				{
					if (!CanOverheat)
					{
						return CurrentFireMode.shotsPerBurst.TrueMax * 3;
					}
					return Mathf.CeilToInt(MaxHeatCapacity / turretDef.cooldown.heatPerShot);
				}
				return CurrentFireMode.shotsPerBurst.RandomInRange;
			}
		}

		public int TicksPerShot
		{
			get
			{
				return CurrentFireMode.ticksBetweenShots;
			}
		}

		//TODO 1.6 - rename
		public float CannonIconAlphaTicked
		{
			get
			{
				if (ReloadTicks <= 0)
				{
					return 1;
				}
				return Mathf.PingPong(ReloadTicks, 25) / 50f + 0.25f; //ping pong between 0.25 and 0.75 alpha
			}
		}

		//TODO 1.6 - rename
		public virtual Material CannonMaterial
		{
			get
			{
				if (cachedMaterial is null)
				{
					ResolveCannonGraphics(vehicle);
				}
				return cachedMaterial;
			}
		}

		//TODO 1.6 - rename
		public virtual Texture2D CannonTexture
		{
			get
			{
				if (CannonGraphicData.texPath.NullOrEmpty())
				{
					return null;
				}
				if (cachedTexture is null)
				{
					cachedTexture = ContentFinder<Texture2D>.Get(CannonGraphicData.texPath);
				}
				return cachedTexture;
			}
		}

		public virtual Texture2D MainMaskTexture
		{
			get
			{
				if (CannonGraphicData.texPath.NullOrEmpty())
				{
					return null;
				}
				if (mainMaskTex is null)
				{
					mainMaskTex = ContentFinder<Texture2D>.Get(CannonGraphicData.texPath + Graphic_Turret.TurretMaskSuffix);
				}
				return mainMaskTex;
			}
		}

		//TODO 1.6 - rename
		public virtual Graphic_Turret CannonGraphic
		{
			get
			{
				if (cachedGraphic is null)
				{
					ResolveCannonGraphics(vehicle);
				}
				return cachedGraphic;
			}
		}

		public virtual List<TurretDrawData> TurretGraphics
		{
			get
			{
				return turretGraphics;
			}
		}

		//TODO 1.6 - rename
		public virtual GraphicDataRGB CannonGraphicData
		{
			get
			{
				if (cachedGraphicData is null)
				{
					ResolveCannonGraphics(vehicle);
				}
				return cachedGraphicData;
			}
		}

		public virtual Texture2D GizmoIcon
		{
			get
			{
				if (gizmoIcon is null)
				{
					if (!string.IsNullOrEmpty(turretDef.gizmoIconTexPath))
					{
						gizmoIcon = ContentFinder<Texture2D>.Get(turretDef.gizmoIconTexPath);
					}
					else if (NoGraphic)
					{
						gizmoIcon = BaseContent.BadTex;
					}
					else
					{
						if (CannonTexture != null)
						{
							gizmoIcon = CannonTexture;
						}
						else
						{
							gizmoIcon = BaseContent.BadTex;
						}
					}
				}
				return gizmoIcon;
			}
		}

		/// <summary>
		/// Smaller actions inside VehicleTurret gizmo
		/// </summary>
		public virtual IEnumerable<SubGizmo> SubGizmos
		{
			get
			{
				if (turretDef.magazineCapacity > 0)
				{
					if (turretDef.ammunition != null)
					{
						yield return SubGizmo_RemoveAmmo(this);
					}
					yield return SubGizmo_ReloadFromInventory(this);
				}

				yield return SubGizmo_FireMode(this);

				if (autoTargeting)
				{
					yield return SubGizmo_AutoTarget(this);
				}
			}
		}

		public Vector3 TurretLocation
		{
			get
			{
				if (attachedTo != null)
				{
					//Don't use cached value if attached to parent turret (position may change with rotations)
					return vehicle.DrawPos + TurretDrawLocFor(vehicle.FullRotation);
				}
				return vehicle.DrawPos + TurretOffset(vehicle.FullRotation);
			}
		}

		public float TurretRotation
		{
			get
			{
				if (!IsTargetable && attachedTo is null)
				{
					return defaultAngleRotated + vehicle.FullRotation.AsAngle;
				}
				ValidateLockStatus();

				rotation = rotation.ClampAndWrap(0, 360);

				if (attachedTo != null)
				{
					return rotation + attachedTo.TurretRotation;
				}
				return rotation;
			}
			set
			{
				rotation = value.ClampAndWrap(0, 360);
			}
		}

		public float TurretRotationTargeted
		{
			get
			{
				return rotationTargeted;
			}
			set
			{
				if (vehicle == null)
				{
					return;
				}
				rotationTargeted = value.ClampAndWrap(0, 360);
			}
		}

		public FireMode CurrentFireMode
		{
			get
			{
				if (currentFireMode < 0 || currentFireMode >= turretDef.fireModes.Count)
				{
					SmashLog.ErrorOnce($"Unable to retrieve fire mode at index {currentFireMode}. Outside of bounds for <field>fireModes</field> defined in <field>turretDef</field>. Defaulting to first fireMode.", GetHashCode() ^ currentFireMode);
					return turretDef.fireModes.FirstOrDefault();
				}
				return turretDef.fireModes[currentFireMode];
			}
			set
			{
				currentFireMode = turretDef.fireModes.IndexOf(value);
				ResetPrefireTimer();
			}
		}

		public bool AutoTarget
		{
			get
			{
				return autoTargetingActive;
			}
			set
			{
				if (!CanAutoTarget || value == autoTargetingActive)
				{
					return;
				}

				autoTargetingActive = value;

				if (autoTargetingActive)
				{
					StartTicking();
				}
			}
		}

		public float MaxRange
		{
			get
			{
				if (turretDef.maxRange < 0)
				{
					return DefaultMaxRange;
				}
				return turretDef.maxRange;
			}
		}

		public float MinRange
		{
			get
			{
				return turretDef.minRange;
			}
		}

		public int MaterialCount => 1;

		public string Name => $"{turretDef}_{key}_{vehicle?.ThingID ?? "Def"}";

		public PatternDef PatternDef
		{
			get
			{
				if (NoGraphic)
				{
					return PatternDefOf.Default;
				}
				if (vehicle == null)
				{
					return VehicleMod.settings.vehicles.defaultGraphics.TryGetValue(vehicleDef.defName, vehicleDef.graphicData)?.patternDef ?? PatternDefOf.Default;
				}
				if (turretDef.matchParentColor)
				{
					return vehicle?.PatternDef ?? PatternDefOf.Default;
				}
				return CannonGraphicData.pattern;
			}
		}

		public void Init(VehicleTurret reference)
		{
			this.reference = reference;
			groupKey = reference.groupKey;
			parentKey = reference.parentKey;

			renderProperties = new VehicleTurretRender(reference.renderProperties);
			if (reference.component != null)
			{
				component = TurretComponentRequirement.CopyFrom(reference.component);
			}

			aimPieOffset = reference.aimPieOffset;
			angleRestricted = reference.angleRestricted;
			restrictedTheta = (int)Mathf.Abs(angleRestricted.x - (angleRestricted.y + 360)).ClampAngle();

			defaultAngleRotated = reference.defaultAngleRotated;
			deployment = reference.deployment;

			drawLayer = reference.drawLayer;
			if (reference.turretDef.restrictionType != null)
			{
				SetTurretRestriction(reference.turretDef.restrictionType);
			}

			ResetAngle();
			LongEventHandler.ExecuteWhenFinished(RecacheRootDrawPos);
		}

		public void SetTurretRestriction(Type type)
		{
			if (!type.IsSubclassOf(typeof(TurretRestrictions)))
			{
				Log.Error($"Trying to create TurretRestriction with non-matching type.");
				return;
			}
			restrictions = (TurretRestrictions)Activator.CreateInstance(type);
			restrictions.Init(vehicle, this);
		}

		public void RemoveTurretRestriction()
		{
			restrictions = null;
		}

		public void OnFieldChanged()
		{
			RecacheRootDrawPos();
		}

		/// <summary>
		/// Caches VehicleTurret draw location based on <see cref="renderProperties"/>, <see cref="attachedTo"/> draw loc is not cached, as rotating can alter the final draw location
		/// </summary>
		public void RecacheRootDrawPos()
		{
			if (CannonGraphicData != null)
			{
				rootDrawPos_North = TurretDrawLocFor(Rot8.North, fullLoc: false);
				rootDrawPos_East = TurretDrawLocFor(Rot8.East, fullLoc: false);
				rootDrawPos_South = TurretDrawLocFor(Rot8.South, fullLoc: false);
				rootDrawPos_West = TurretDrawLocFor(Rot8.West, fullLoc: false);
				rootDrawPos_NorthEast = TurretDrawLocFor(Rot8.NorthEast, fullLoc: false);
				rootDrawPos_SouthEast = TurretDrawLocFor(Rot8.SouthEast, fullLoc: false);
				rootDrawPos_SouthWest = TurretDrawLocFor(Rot8.SouthWest, fullLoc: false);
				rootDrawPos_NorthWest = TurretDrawLocFor(Rot8.NorthWest, fullLoc: false);
			}
		}

		public Vector3 TurretOffset(Rot8 rot)
		{
			return rot.AsInt switch
			{
				//North
				0 => rootDrawPos_North,
				//East
				1 => rootDrawPos_East,
				//South
				2 => rootDrawPos_South,
				//West
				3 => rootDrawPos_West,
				//NorthEast
				4 => rootDrawPos_NorthEast,
				//SouthEast
				5 => rootDrawPos_SouthEast,
				//SouthWest
				6 => rootDrawPos_SouthWest,
				//NorthWest
				7 => rootDrawPos_NorthWest,
				//Should not reach here
				_ => throw new NotImplementedException("Invalid Rot8")
			};
		}

		public void RecacheMannedStatus()
		{
			IsManned = true;
			foreach (VehicleHandler handler in vehicle.handlers)
			{
				if (handler.role.HandlingTypes.HasFlag(HandlingTypeFlags.Turret) && (handler.role.TurretIds.Contains(key) || handler.role.TurretIds.Contains(groupKey)))
				{
					if (!handler.RoleFulfilled)
					{
						IsManned = false;
						break;
					}
				}
			}
			IsManned |= VehicleMod.settings.debug.debugShootAnyTurret; //Only if debug shoot any turret = true, set satisfied to true anyways.
		}

		public bool GroupsWith(VehicleTurret turret)
		{
			return !groupKey.NullOrEmpty() && groupKey == turret.groupKey;
		}

		public Vector3 TurretDrawLocFor(Rot8 rot, bool fullLoc = true)
		{
			float locationRotation = 0f;
			if (fullLoc && attachedTo != null)
			{
				locationRotation = TurretRotationFor(rot, attachedTo.TurretRotation);
			}
			Vector2 turretLoc = VehicleGraphics.TurretDrawOffset(rot, renderProperties, locationRotation, fullLoc ? attachedTo : null);
			Vector3 graphicOffset = CannonGraphic?.DrawOffset(rot) ?? Vector3.zero;
			return new Vector3(graphicOffset.x + turretLoc.x, graphicOffset.y + DrawLayerOffset, graphicOffset.z + turretLoc.y);
		}

		public Rect ScaleUIRectRecursive(VehicleDef vehicleDef, Rect rect, Rot8 rot, float iconScale = 1)
		{
			//Scale to VehicleDef drawSize
			Vector2 size = vehicleDef.ScaleDrawRatio(turretDef.graphicData, rot, rect.size, iconScale: iconScale);
			//Adjust position from new rect size
			Vector2 adjustedPosition = rect.position + (rect.size - size) / 2f;
			// Size / V_max = scalar
			float scalar = rect.size.x / Mathf.Max(vehicleDef.graphicData.drawSize.x, vehicleDef.graphicData.drawSize.y);
			Vector2 offset = rot.AsInt switch
			{
				//North
				0 => renderProperties.North,
				//East
				1 => renderProperties.East,
				//South
				2 => renderProperties.South,
				//West
				3 => renderProperties.West,
				//Diagonals not handled
				_ => throw new NotImplementedException("Diagonal rotations in UI"),
			};

			Vector3 graphicOffset = turretDef.graphicData.DrawOffsetForRot(rot);
			
			Vector2 position = adjustedPosition + (scalar * new Vector2(graphicOffset.x, graphicOffset.z));
			Vector2 offsetPosition = scalar * offset;
			Vector2 parentPosition = Vector2.zero;
			if (attachedTo != null)
			{
				parentPosition = attachedTo.ScaleUIRectRecursive(vehicleDef, rect, rot).position; //Recursively adjust from parent positions
				float parentRotation = TurretRotationFor(rot, attachedTo.TurretRotation);
				offsetPosition = Ext_Math.RotatePointClockwise(offsetPosition, parentRotation); //Rotate around parent's position
			}
			offsetPosition.y *= -1; //Invert y axis post-calculations, UI y-axis is top to bottom
			offsetPosition += parentPosition;
			position += offsetPosition;
			return new Rect(position, size);
		}

		public static float TurretRotationFor(Rot8 rot, float currentRotation)
		{
			return currentRotation + rot.AsAngle;
		}

		//REDO - disable type implementation
		public virtual bool TurretEnabled(VehicleDef vehicleDef, TurretDisableType turretKey)
		{
			if (conditionalTurrets.Contains(new Pair<string, TurretDisableType>(vehicleDef.defName, turretKey)))
			{

			}
			return false;
		}

		public virtual bool ActivateTimer(bool ignoreTimer = false)
		{
			if (ReloadTicks > 0 && !ignoreTimer)
			{
				return false;
			}
			reloadTicks = MaxTicks;
			TargetLocked = false;
			StartTicking();
			return true;
		}

		public virtual void ActivateBurstTimer()
		{
			burstTicks = CurrentFireMode.ticksBetweenBursts.RandomInRange;
			burstsTillWarmup--;
			
			if (burstsTillWarmup <= 0)
			{
				ResetPrefireTimer();
			}
		}

		public void StartTicking()
		{
			vehicle.CompVehicleTurrets.QueueTicker(this);
		}

		/// <summary>
		/// Should only be called in the event that this turret needs to stop ticking unconditionally, otherwise let <see cref="CompVehicleTurrets"/> dequeue
		/// when it's determined this turret no longer requires ticking.
		/// </summary>
		public void StopTicking()
		{
			vehicle.CompVehicleTurrets.DequeueTicker(this);
		}

		public virtual bool Tick()
		{
			bool cooldownTicked = TurretCooldownTick();
			bool reloadTicked = TurretReloadTick();
			bool rotationTicked = TurretRotationTick();
			bool targeterTicked = TurretTargeterTick();
			bool autoTicked = TurretAutoTick();
			bool recoilTicked = false;
			if (recoilTracker != null)
			{
				recoilTicked = recoilTracker.RecoilTick();
			}
			if (!recoilTrackers.NullOrEmpty())
			{
				for (int i = 0; i < turretDef.graphics.Count; i++)
				{
					recoilTicked |= recoilTrackers[i]?.RecoilTick() ?? false;
				}
			}
			//Keep ticking until no longer needed
			return cooldownTicked || reloadTicked || autoTicked || rotationTicked || targeterTicked || recoilTicked;
		}

		protected virtual bool TurretCooldownTick()
		{
			if (CanOverheat)
			{
				if (currentHeatRate > 0)
				{
					ticksSinceLastShot++;
				}

				if (currentHeatRate > MaxHeatCapacity)
				{
					triggeredCooldown = true;
					currentHeatRate = MaxHeatCapacity;
					EventRegistry[VehicleTurretEventDefOf.Cooldown].ExecuteEvents();
				}
				else if (currentHeatRate <= 0)
				{
					currentHeatRate = 0;
					triggeredCooldown = false;
					return false;
				}

				if (ticksSinceLastShot >= TicksTillBeginCooldown)
				{
					float dissipationRate = turretDef.cooldown.dissipationRate;
					if (triggeredCooldown)
					{
						dissipationRate *= turretDef.cooldown.dissipationCapMultiplier;
					}
					currentHeatRate -= dissipationRate;
				}
				return true;
			}
			return false;
		}

		protected virtual bool TurretReloadTick()
		{
			if (vehicle.Spawned && !queuedToFire)
			{
				if (ReloadTicks > 0 && !OnCooldown)
				{
					reloadTicks--;
					return true;
				}
				if (burstTicks > 0)
				{
					burstTicks--;
					return true;
				}
			}
			return false;
		}

		protected virtual bool TurretAutoTick()
		{
			if (vehicle.Spawned && !queuedToFire && AutoTarget)
			{
				if (Find.TickManager.TicksGame % AutoTargetInterval == 0)
				{
					if (TurretDisabled)
					{
						return false;
					}
					if (!cannonTarget.IsValid && TurretTargeter.Turret != this && ReloadTicks <= 0 && HasAmmo)
					{
						if (this.TryGetTarget(out LocalTargetInfo autoTarget, additionalFlags: TargetScanFlags.NeedAutoTargetable))
						{
							AlignToAngleRestricted(TurretLocation.AngleToPoint(autoTarget.Thing.DrawPos));
							SetTarget(autoTarget);
						}
					}
				}
				return true;
			}
			return false;
		}

		protected virtual bool TurretRotationTick()
		{
			bool tick = false;
			if (TargetLocked)
			{
				AlignToTargetRestricted();
				tick = true;
			}
			if (!RotationAligned)
			{
				if (ComponentDisabled)
				{
					ResetAngle();
					return false;
				}
				if (Math.Abs(rotation - TurretRotationTargeted) < turretDef.rotationSpeed + 0.1f)
				{
					rotation = TurretRotationTargeted;
				}
				else
				{
					int rotationDir;
					if (rotation < TurretRotationTargeted)
					{
						if (Math.Abs(rotation - TurretRotationTargeted) < 180)
						{
							rotationDir = 1;
						}
						else
						{
							rotationDir = -1;
						}
					}
					else
					{
						if (Math.Abs(rotation - TurretRotationTargeted) < 180)
						{
							rotationDir = -1;
						}
						else
						{
							rotationDir = 1;
						}
					}
					
					rotation += turretDef.rotationSpeed * rotationDir;
					foreach (VehicleTurret turret in childTurrets)
					{
						turret.rotation += turretDef.rotationSpeed * rotationDir;
					}
				}
				return true;
			}
			return tick;
		}

		protected virtual bool TurretTargeterTick()
		{
			if (TurretTargetValid)
			{
				if (rotation == TurretRotationTargeted && !TargetLocked)
				{
					TargetLocked = true;
					ResetPrefireTimer();
				}
				else if (!TurretTargetValid)
				{
					SetTarget(LocalTargetInfo.Invalid);
					return TurretTargeter.Turret == this;
				}

				if (IsTargetable && !TargetingHelper.TargetMeetsRequirements(this, cannonTarget))
				{
					SetTarget(LocalTargetInfo.Invalid);
					TargetLocked = false;
					return TurretTargeter.Turret == this;
				}
				if (PrefireTickCount > 0)
				{
					if (cannonTarget.HasThing)
					{
						TurretRotationTargeted = TurretLocation.AngleToPoint(cannonTarget.Thing.DrawPos);
						if (attachedTo != null)
						{
							TurretRotationTargeted -= attachedTo.TurretRotation;
						}
					}
					else
					{
						TurretRotationTargeted = TurretLocation.ToIntVec3().AngleToCell(cannonTarget.Cell);
						if (attachedTo != null)
						{
							TurretRotationTargeted -= attachedTo.TurretRotation;
						}
					}

					if (turretDef.autoSnapTargeting)
					{
						rotation = TurretRotationTargeted;
					}

					if (TargetLocked && ReadyToFire)
					{
						PrefireTickCount--;
					}
				}
				else if (ReadyToFire)
				{
					if (IsTargetable && RotationAligned && (cannonTarget.Pawn is null || !CheckTargetInvalid()))
					{
						GroupTurrets.ForEach(t => t.PushTurretToQueue());
					}
					else if (FullAuto)
					{
						GroupTurrets.ForEach(t => t.PushTurretToQueue());
					}
				}
				return true;
			}
			else if (IsTargetable)
			{
				return TurretTargeter.Turret == this;
			}
			return false;
		}

		public virtual CompVehicleTurrets.TurretData GenerateTurretData()
		{
			return new CompVehicleTurrets.TurretData()
			{
				shots = CurrentFireMode.shotsPerBurst.RandomInRange,
				ticksTillShot = 0,
				turret = this
			};
		}

		public virtual void PushTurretToQueue()
		{
			ActivateBurstTimer();
			vehicle.CompVehicleTurrets.QueueTurret(GenerateTurretData());
		}

		public static bool TryFindShootLineFromTo(IntVec3 root, LocalTargetInfo targ, out ShootLine resultingLine)
		{
			resultingLine = new ShootLine(root, targ.Cell);
			return false;
		}

		public virtual void FireTurret()
		{
			if (!vehicle.Spawned)
			{
				return;
			}
			TryFindShootLineFromTo(TurretLocation.ToIntVec3(), cannonTarget, out ShootLine shootLine);
			
			float range = Vector3.Distance(TurretLocation, cannonTarget.CenterVector3);
			IntVec3 cell = cannonTarget.Cell;//
			if (CurrentFireMode.spreadRadius > 0)
			{
				int cellsInRadius;
				if (turretDef.maxRange > 0)
				{
					cellsInRadius = GenRadial.NumCellsInRadius(CurrentFireMode.spreadRadius * (range / turretDef.maxRange));
				}
				else
				{
					cellsInRadius = GenRadial.NumCellsInRadius(CurrentFireMode.spreadRadius);
				}
				cell += GenRadial.RadialPattern[Rand.Range(0, cellsInRadius)];
			}
			if (CurrentTurretFiring >= turretDef.projectileShifting.Count)
			{
				CurrentTurretFiring = 0;
			}
			float horizontalOffset = turretDef.projectileShifting.NotNullAndAny() ? turretDef.projectileShifting[CurrentTurretFiring] : 0;
			Vector3 launchCell = TurretLocation + new Vector3(horizontalOffset, 1f, turretDef.projectileOffset).RotatedBy(TurretRotation);

			ThingDef projectileDef = ProjectileDef;// turretDef.projectile;
			//if (turretDef.ammunition != null && !turretDef.genericAmmo)
			//{
			//	projectile = loadedAmmo?.projectileWhenLoaded ?? projectile; //nc to loaded ammo for CE handling
			//}
			try
			{
				if (turretDef.ammunition != null)
				{
					ConsumeShellChambered();
				}
				if (LaunchProjectileCE is null)
				{
					Projectile projectileInstance = (Projectile)GenSpawn.Spawn(projectileDef, vehicle.Position, vehicle.Map, WipeMode.Vanish);
					if (turretDef.projectileSpeed > 0)
					{
						projectileInstance.TryAddComp(new CompTurretProjectileProperties(projectileInstance)
						{
							speed = turretDef.projectileSpeed > 0 ? turretDef.projectileSpeed : projectileInstance.def.projectile.speed,
							hitflag = turretDef.hitFlags,
							hitflags = turretDef.attachProjectileFlag
						});
					}
					projectileInstance.Launch(vehicle, launchCell, cell, cannonTarget, projectileInstance.HitFlags, false, vehicle);
				}
				else
				{
					float speed = turretDef.projectileSpeed > 0 ? turretDef.projectileSpeed : projectileDef.projectile.speed;
					float swayAndSpread = Mathf.Atan2(CurrentFireMode.spreadRadius, MaxRange) * Mathf.Rad2Deg;
					float sway = swayAndSpread * 0.84f;
					float spread = swayAndSpread * 0.16f;
					float recoil = horizontalOffset / MaxRange;
					float shotHeight = 1f;
					CETurretDataDefModExtension turretData = turretDef.GetModExtension<CETurretDataDefModExtension>();
					if (turretData != null)
					{
						if (turretData.speed > 0)
						{
							speed = turretData.speed;
						}
						if (turretData.sway >= 0)
						{
							sway = turretData.sway;
						}
						if (turretData.spread >= 0)
						{
							spread = turretData.spread;
						}
						recoil = turretData.recoil;
						shotHeight = turretData.shotHeight;
						if (turretData._ammoSet == null && turretData.ammoSet != null) {
						    turretData._ammoSet = LookupAmmosetCE(turretData.ammoSet);
						}
					}
					int projectileCount = 1;
					if (LookupProjectileCountAndSpreadCE != null)
					{
						var pcs = LookupProjectileCountAndSpreadCE(loadedAmmo, turretData?._ammoSet, spread);
						projectileCount = pcs.Item1;
						spread = pcs.Item2;
					}

					float distance = (launchCell - cannonTarget.CenterVector3).magnitude;

					Vector2 vce = ProjectileAngleCE(speed, distance, vehicle, cannonTarget, new Vector3(launchCell.x, shotHeight, launchCell.z), false, 1f, sway, 0, recoil * CurrentTurretFiring);
					float sa = vce.y;
					float tr = -TurretRotation + vce.x;
					do {
						double randomSpread = Rand.Value * spread;
						double spreadDirection = Rand.Value * Math.PI * 2;
						vce.y = (float)(randomSpread * Math.Sin(spreadDirection));
						vce.x = (float)(randomSpread * Math.Cos(spreadDirection));
						LaunchProjectileCE(projectileDef, loadedAmmo, turretData?._ammoSet, new Vector2(launchCell.x, launchCell.z), cannonTarget, vehicle, sa + vce.y * Mathf.Deg2Rad, tr + vce.x, shotHeight, speed);
					}
					while (--projectileCount > 0);

					if (NotifyShotFiredCE != null)
					{
						NotifyShotFiredCE(projectileDef, loadedAmmo, turretData?._ammoSet, this, recoil);
					}
				}
				turretDef.shotSound?.PlayOneShot(new TargetInfo(vehicle.Position, vehicle.Map));
				vehicle.Drawer.rTracker.Notify_TurretRecoil(this, Ext_Math.RotateAngle(TurretRotation, 180));
				
				if (recoilTracker != null)
				{
					recoilTracker.Notify_TurretRecoil(Ext_Math.RotateAngle(TurretRotation, 180));
				}
				if (!recoilTrackers.NullOrEmpty())
				{
					for (int i = 0; i < recoilTrackers.Length; i++)
					{
						if (recoilTrackers[i] != null)
						{
							recoilTrackers[i].Notify_TurretRecoil(Ext_Math.RotateAngle(TurretRotation, 180));
						}
					}
				}

				EventRegistry[VehicleTurretEventDefOf.ShotFired].ExecuteEvents();
				PostTurretFire();
				InitTurretMotes(launchCell);
			}
			catch (Exception ex)
			{
				Log.Error($"Exception when firing VehicleTurret: {turretDef.defName} on vehicle: {vehicle}.\nException: {ex}");
			}
		}

		public virtual void PostTurretFire()
		{
			ticksSinceLastShot = 0;
			if (CanOverheat)
			{
				currentHeatRate += turretDef.cooldown.heatPerShot;
			}
		}

		public virtual void InitTurretMotes(Vector3 loc)
		{
			if (!turretDef.motes.NullOrEmpty())
			{
				foreach (AnimationProperties moteProps in turretDef.motes)
				{
					Vector3 moteLoc = loc;
					if (loc.ShouldSpawnMotesAt(vehicle.Map))
					{
						try
						{
							float altitudeLayer = Altitudes.AltitudeFor(moteProps.moteDef.altitudeLayer);
							Vector3 offset = moteProps.offset.RotatedBy(TurretRotation);
							moteLoc += new Vector3(offset.x, altitudeLayer + offset.y, offset.z);
							Mote mote = (Mote)ThingMaker.MakeThing(moteProps.moteDef);
							mote.exactPosition = moteLoc;
							mote.exactRotation = moteProps.exactRotation.RandomInRange;
							mote.instanceColor = moteProps.color;
							mote.rotationRate = moteProps.rotationRate;
							mote.Scale = moteProps.scale;
							if (mote is MoteThrown thrownMote)
							{
								float thrownAngle = TurretRotation + moteProps.angleThrown.RandomInRange;
								if (thrownMote is MoteThrownExpand expandMote)
								{
									if (expandMote is MoteThrownSlowToSpeed accelMote)
									{
										accelMote.SetDecelerationRate(moteProps.deceleration.RandomInRange, moteProps.fixedAcceleration, thrownAngle);
									}
									expandMote.growthRate = moteProps.growthRate.RandomInRange;
								}
								thrownMote.SetVelocity(thrownAngle, moteProps.speedThrown.RandomInRange);
							}
							if (mote is MoteCannonPlume cannonMote)
							{
								cannonMote.cyclesLeft = moteProps.cycles;
								cannonMote.animationType = moteProps.animationType;
								cannonMote.angle = TurretRotation;
							}
							mote.def = moteProps.moteDef;
							mote.PostMake();
							GenSpawn.Spawn(mote, moteLoc.ToIntVec3(), vehicle.Map, WipeMode.Vanish);
						}
						catch (Exception ex)
						{
							SmashLog.Error($"Failed to spawn mote at {loc}. MoteDef = <field>{moteProps.moteDef?.defName ?? "Null"}</field> Exception = {ex}");
						}
					}
				}
			}
		}

		public virtual void InitRecoilTrackers()
		{
			if (turretDef.recoil != null)
			{
				recoilTracker = new Turret_RecoilTracker(turretDef.recoil);
			}
			if (!turretDef.graphics.NullOrEmpty())
			{
				recoilTrackers = new Turret_RecoilTracker[turretDef.graphics.Count];
				for (int i = 0; i < turretDef.graphics.Count; i++)
				{
					if (turretDef.graphics[i].recoil is RecoilProperties recoilProperties)
					{
						recoilTrackers[i] = new Turret_RecoilTracker(recoilProperties);
					}
				}
			}
		}

		public virtual void DrawAt(Vector3 drawPos, Rot8 rot)
		{
			if (!NoGraphic)
			{
				VehicleGraphics.DrawTurret(this, drawPos, rot);
			}
			DrawTargeter();
			DrawAimPie();
		}

		public virtual void Draw()
		{
			if (!NoGraphic)
			{
				VehicleGraphics.DrawTurret(this, vehicle.FullRotation);
			}
			DrawTargeter();
			DrawAimPie();
		}

		protected virtual void DrawTargeter()
		{
			if (GizmoHighlighted || TurretTargeter.Turret == this)
			{
				if (angleRestricted != Vector2.zero)
				{
					VehicleGraphics.DrawAngleLines(TurretLocation, angleRestricted, MinRange, MaxRange, restrictedTheta, attachedTo?.TurretRotation ?? vehicle.FullRotation.AsAngle);
				}
				else if (turretDef.turretType == TurretType.Static)
				{
					if (!groupKey.NullOrEmpty())
					{
						foreach (VehicleTurret turret in GroupTurrets)
						{
							Vector3 target = turret.TurretLocation.PointFromAngle(turret.MaxRange, turret.TurretRotation);
							float range = Vector3.Distance(turret.TurretLocation, target);
							GenDraw.DrawRadiusRing(target.ToIntVec3(), turret.CurrentFireMode.spreadRadius * (range / turret.turretDef.maxRange));
						}
					}
					else
					{
						Vector3 target = TurretLocation.PointFromAngle(MaxRange, TurretRotation);
						float range = Vector3.Distance(TurretLocation, target);
						GenDraw.DrawRadiusRing(target.ToIntVec3(), CurrentFireMode.spreadRadius * (range / turretDef.maxRange));
					}
				}
				else
				{
					if (MaxRange > -1)
					{
						Vector3 pos = TurretLocation;
						pos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
						float currentAlpha = 0.65f;
						if (currentAlpha > 0f)
						{
							Color value = Color.grey;
							value.a *= currentAlpha;
							MatPropertyBlock.SetColor(ShaderPropertyIDs.Color, value);
							Matrix4x4 matrix = default;
							matrix.SetTRS(pos, Quaternion.identity, new Vector3(MaxRange * 2f, 1f, MaxRange * 2f));
							Graphics.DrawMesh(MeshPool.plane10, matrix, TexData.RangeMat((int)MaxRange), 0, null, 0, MatPropertyBlock);
						}


					}
					if (MinRange > 0)
					{
						Vector3 pos = TurretLocation;
						pos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
						float currentAlpha = 0.65f;
						if (currentAlpha > 0f)
						{
							Color value = Color.red;
							value.a *= currentAlpha;
							MatPropertyBlock.SetColor(ShaderPropertyIDs.Color, value);
							Matrix4x4 matrix = default;
							matrix.SetTRS(pos, Quaternion.identity, new Vector3(MinRange * 2f, 1f, MinRange * 2f));
							Graphics.DrawMesh(MeshPool.plane10, matrix, TexData.RangeMat((int)MinRange), 0, null, 0, MatPropertyBlock);
						}
					}
				}
			}
		}

		protected virtual void DrawAimPie()
		{
			if (TargetLocked && ReadyToFire && Find.Selector.SingleSelectedThing == vehicle)
			{
				float facing = cannonTarget.Thing != null ? (cannonTarget.Thing.DrawPos - TurretLocation).AngleFlat() : (cannonTarget.Cell - TurretLocation.ToIntVec3()).AngleFlat;
				GenDraw.DrawAimPieRaw(TurretLocation + new Vector3(aimPieOffset.x, Altitudes.AltInc, aimPieOffset.y).RotatedBy(TurretRotation), facing, (int)(PrefireTickCount * 0.5f));
			}
		}

		public virtual void ResolveCannonGraphics(VehiclePawn vehicle, bool forceRegen = false)
		{
			ResolveCannonGraphics(vehicle.patternData, forceRegen: forceRegen);
		}

		public virtual void ResolveCannonGraphics(VehicleDef vehicleDef, bool forceRegen = false)
		{
			PatternData patternData = VehicleMod.settings.vehicles.defaultGraphics.TryGetValue(vehicleDef.defName, vehicleDef.graphicData);
			ResolveCannonGraphics(patternData, forceRegen: forceRegen);
		}

		public virtual void ResolveCannonGraphics(PatternData patternData, bool forceRegen = false)
		{
			if (NoGraphic)
			{
				return;
			}
			if (cachedGraphicData is null || forceRegen)
			{
				cachedGraphic = GenerateGraphicData(this, this, turretDef.graphicData, patternData, ref cachedGraphicData);
				if (!turretDef.graphics.NullOrEmpty())
				{
					SetLayerGraphics(patternData);
				}
			}
			if (cachedMaterial is null || forceRegen)
			{
				cachedMaterial = CannonGraphic.MatAt(Rot8.North, vehicle);
			}
		}

		private void SetLayerGraphics(PatternData patternData)
		{
			if (turretGraphics.NullOrEmpty())
			{
				turretGraphics ??= new List<TurretDrawData>();
				for (int i = 0; i < turretDef.graphics.Count; i++)
				{
					turretGraphics.Add(new TurretDrawData(this, turretDef.graphics[i]));
				}
			}
			for (int i = 0; i < turretDef.graphics.Count; i++)
			{
				VehicleTurretRenderData renderData = turretDef.graphics[i];
				TurretDrawData drawData = TurretGraphics[i];
				drawData.Set(renderData.graphicData, patternData);
			}
		}

		private static Graphic_Turret GenerateGraphicData(IMaterialCacheTarget cacheTarget, VehicleTurret turret, GraphicDataRGB copyGraphicData, PatternData patternData, ref GraphicDataRGB cachedGraphicData)
		{
			cachedGraphicData = new GraphicDataRGB();
			cachedGraphicData.CopyFrom(copyGraphicData);
			Graphic_Turret graphic;
			if ((cachedGraphicData.shaderType.Shader.SupportsMaskTex() || cachedGraphicData.shaderType.Shader.SupportsRGBMaskTex()))
			{
				if (turret.turretDef.matchParentColor)
				{
					cachedGraphicData.CopyDrawData(patternData);
				}
				else
				{
					cachedGraphicData.CopyDrawData(copyGraphicData);
				}
			}
			if (cachedGraphicData.shaderType != null && cachedGraphicData.shaderType.Shader.SupportsRGBMaskTex())
			{
				RGBMaterialPool.CacheMaterialsFor(cacheTarget, patternData.patternDef);
				cachedGraphicData.Init(cacheTarget);
				graphic = cachedGraphicData.Graphic as Graphic_Turret;
				RGBMaterialPool.SetProperties(cacheTarget, cachedGraphicData, graphic.TexAt, graphic.MaskAt);
			}
			else
			{
				graphic = ((GraphicData)cachedGraphicData).Graphic as Graphic_Turret;
			}
			return graphic;
		}

		public bool AngleBetween(Vector3 mousePosition)
		{
			if (angleRestricted == Vector2.zero)
			{
				return true;
			}

			float rotationOffset = attachedTo != null ? attachedTo.TurretRotation : vehicle.Rotation.AsAngle + vehicle.Angle;

			float start = angleRestricted.x + rotationOffset;
			float end = angleRestricted.y + rotationOffset;

			if (start > 360)
			{
				start -= 360;
			}
			if (end > 360)
			{
				end -= 360;
			}

			float mid = (mousePosition - TurretLocation).AngleFlat();
			end = (end - start) < 0f ? end - start + 360 : end - start;
			mid = (mid - start) < 0f ? mid - start + 360 : mid - start;
			return mid < end;
		}

		public bool InRange(LocalTargetInfo target)
		{
			if (MinRange == 0 && MaxRange == DefaultMaxRange)
			{
				return true;
			}
			IntVec3 cell = target.Cell;
			Vector2 targetPos = new Vector2(cell.x, cell.z);
			Vector3 targetLoc = TurretLocation;
			float distance = Vector2.Distance(new Vector2(targetLoc.x, targetLoc.z), targetPos);

			return distance >= MinRange && distance <= MaxRange;
		}
		
		public void AlignToTargetRestricted()
		{
			if (cannonTarget.HasThing)
			{
				TurretRotationTargeted = TurretLocation.AngleToPoint(cannonTarget.Thing.DrawPos);
				if (attachedTo != null)
				{
					TurretRotationTargeted -= attachedTo.TurretRotation;
				}
			}
			else
			{
				TurretRotationTargeted = TurretLocation.ToIntVec3().AngleToCell(cannonTarget.Cell);
				if (attachedTo != null)
				{
					TurretRotationTargeted -= attachedTo.TurretRotation;
				}
			}
		}

		public void AlignToAngleRestricted(float angle)
		{
			float additionalAngle = attachedTo?.TurretRotation ?? 0;
			if (turretDef.autoSnapTargeting)
			{
				TurretRotation = angle - additionalAngle;
				TurretRotationTargeted = rotation;
			}
			else
			{
				TurretRotationTargeted = (angle - additionalAngle).ClampAndWrap(0, 360);
			}
		}

		public virtual void ReloadCannon(ThingDef ammo = null, bool ignoreTimer = false)
		{
			if ( (ammo == savedAmmoType || ammo is null) && shellCount == turretDef.magazineCapacity)
			{
				return;
			}
			if (turretDef.ammunition is null)
			{
				shellCount = turretDef.magazineCapacity;
				return;
			}
			if (loadedAmmo is null || (ammo != null && shellCount < turretDef.magazineCapacity) || shellCount <= 0 || ammo != null)
			{
				if (ReloadInternal(ammo))
				{
					ActivateTimer(ignoreTimer);
				}
				else
				{
					Messages.Message("VF_NoAmmoAvailable".Translate(), MessageTypeDefOf.RejectInput);
				}
			}
		}

		/// <summary>
		/// Automatically reload magazine of VehicleTurret with first Ammo Type in inventory
		/// </summary>
		/// <returns>True if Cannon has been successfully reloaded.</returns>
		public virtual bool AutoReloadCannon()
		{
			ThingDef ammoType = vehicle.inventory.innerContainer.FirstOrDefault(t => turretDef.ammunition.Allows(t) || turretDef.ammunition.Allows(t.def.projectileWhenLoaded))?.def;
			if (ammoType != null)
			{
				return ReloadInternal(ammoType);
			}
			Debug.Warning($"Failed to auto-reload {turretDef.label}");
			return false;
		}

		public void SetMagazineCount(int count)
		{
			shellCount = Mathf.Clamp(count, 0, turretDef.magazineCapacity);
		}

		protected bool ReloadInternal(ThingDef ammo)
		{
			try
			{
				if (vehicle.inventory.innerContainer.Contains(savedAmmoType) || vehicle.inventory.innerContainer.Contains(ammo))
				{
					//Remembers previously stored ammo for auto-loading feature
					Thing storedAmmo = null;
					if (ammo != null)
					{
						storedAmmo = vehicle.inventory.innerContainer.FirstOrFallback(x => x.def == ammo);
						savedAmmoType = ammo;
						TryRemoveShell();
					}
					else if (savedAmmoType != null)
					{
						storedAmmo = vehicle.inventory.innerContainer.FirstOrFallback(x => x.def == savedAmmoType);
					}
					else
					{
						Log.Error("No saved or specified shell upon reload");
						return false;
					}

					int countToRefill = turretDef.magazineCapacity - shellCount;
					int countToTake = Mathf.CeilToInt(countToRefill * turretDef.chargePerAmmoCount);
					int countRefilled = 0;

					thingsToTakeReloading.Clear();
					{
						//Deterine which items (and how much) to take
						foreach (Thing thing in vehicle.inventory.innerContainer)
						{
							if (thing.def == storedAmmo.def)
							{
								int availableCount = thing.stackCount - thing.stackCount % Mathf.CeilToInt(turretDef.chargePerAmmoCount);
								int takingFromThing = Mathf.Min(countToTake, availableCount);
								thingsToTakeReloading.Add((thing, takingFromThing));
								countToTake -= takingFromThing;
								if (countToTake <= 0) break;
							}
						}
						//Quick check to make sure to not even bother removing items from inventory if there is not enough to reload 1 shot minimum
						if (thingsToTakeReloading.Sum(pair => pair.Item2) < turretDef.chargePerAmmoCount)
						{
							return false;
						}
						//Take items from inventory without going over the amount required
						for (int i = thingsToTakeReloading.Count - 1; i >= 0; i--)
						{
							if (thingsToTakeReloading.Sum(pair => pair.Item2) < turretDef.chargePerAmmoCount)
							{
								break;
							}

							(Thing thing, int count) = thingsToTakeReloading[i];
							countRefilled += count;
							vehicle.TakeFromInventory(thing, count);
							thingsToTakeReloading.RemoveAt(i);
						}
					}
					thingsToTakeReloading.Clear();

					if (countRefilled % turretDef.chargePerAmmoCount != 0)
					{
						Log.Warning($"Taking more than necessary to reload {this}. This is not supposed to occur. CountRefilled={countRefilled} CountNeeded={countToRefill * turretDef.chargePerAmmoCount}");
					}

					loadedAmmo = storedAmmo.def;
					shellCount = Mathf.CeilToInt(countRefilled / turretDef.chargePerAmmoCount).Clamp(0, turretDef.magazineCapacity);
					EventRegistry[VehicleTurretEventDefOf.Reload].ExecuteEvents();
					if (turretDef.reloadSound != null)
					{
						turretDef.reloadSound.PlayOneShot(new TargetInfo(vehicle.Position, vehicle.Map, false));
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error($"Unable to reload Cannon: {uniqueID} on Pawn: {vehicle.LabelShort}. Exception: {ex}");
				return false;
			}
			return true;
		}

		public void ConsumeShellChambered()
		{
			shellCount--;
			if (shellCount <= 0 && vehicle.inventory.innerContainer.FirstOrFallback(x => x.def == loadedAmmo) is null)
			{
				loadedAmmo = null;
				shellCount = 0;
			}
		}

		public virtual void TryRemoveShell()
		{
			if (loadedAmmo != null && shellCount > 0)
			{
				Thing thing = ThingMaker.MakeThing(loadedAmmo);
				thing.stackCount = Mathf.CeilToInt(shellCount * turretDef.chargePerAmmoCount);
				//vehicle.inventory.innerContainer.TryAdd(thing);
				vehicle.AddOrTransfer(thing);
				loadedAmmo = null;
				shellCount = 0;
				ActivateTimer(true);
			}
		}

		public void CycleFireMode()
		{
			SoundDefOf.Click.PlayOneShotOnCamera(vehicle.Map);
			currentFireMode++;
			if (currentFireMode >= turretDef.fireModes.Count)
			{
				currentFireMode = 0;
			}
		}

		public virtual void SwitchAutoTarget()
		{
			if (CanAutoTarget)
			{
				SoundDefOf.Click.PlayOneShotOnCamera(vehicle.Map);
				AutoTarget = !AutoTarget;
				SetTarget(LocalTargetInfo.Invalid);
				if (AutoTarget)
				{
					StartTicking();
				}
			}
			else
			{
				Messages.Message("VF_AutoTargetingDisabled".Translate(), MessageTypeDefOf.RejectInput);
			}
		}

		public virtual void SetTarget(LocalTargetInfo target)
		{
			cannonTarget = target;
			TargetLocked = false;
			if (target.Pawn is Pawn)
			{
				if (target.Pawn.Downed)
				{
					CachedPawnTargetStatus = PawnStatusOnTarget.Down;
				}
				else if (target.Pawn.Dead)
				{
					CachedPawnTargetStatus = PawnStatusOnTarget.Dead;
				}
				else
				{
					CachedPawnTargetStatus = PawnStatusOnTarget.Alive;
				}
			}
			else
			{
				CachedPawnTargetStatus = PawnStatusOnTarget.None;
			}

			if (cannonTarget.IsValid)
			{
				StartTicking();
			}
		}

		/// <summary>
		/// Set target only if cannonTarget is no longer valid or if target is cell based
		/// </summary>
		/// <param name="target"></param>
		/// <returns>true if cannonTarget set to target, false if target is still valid</returns>
		public virtual bool CheckTargetInvalid(bool resetPrefireTimer = true)
		{
			if (cannonTarget.IsValid && (cannonTarget.HasThing || FullAuto))
			{
				if (cannonTarget.Pawn != null)
				{
					if ( (cannonTarget.Pawn.Dead && CachedPawnTargetStatus != PawnStatusOnTarget.Dead ) || (cannonTarget.Pawn.Downed && CachedPawnTargetStatus != PawnStatusOnTarget.Down) )
					{
						SetTarget(LocalTargetInfo.Invalid);
						return true;
					}
				}
				else if (cannonTarget.Thing != null && cannonTarget.Thing.HitPoints <= 0)
				{
					SetTarget(LocalTargetInfo.Invalid);
					return true;
				}
				return false;
			}
			return false;
		}

		public void ResetAngle()
		{
			rotation = rotation.ClampAndWrap(0, 360);
			TurretRotationTargeted = rotation;
		}

		public void FlagForAlignment()
		{
			TurretRotationTargeted = TurretRotationFor(vehicle.FullRotation, defaultAngleRotated.ClampAndWrap(0, 360));
		}

		public virtual void ResetPrefireTimer()
		{
			PrefireTickCount = WarmupTicks;
			EventRegistry[VehicleTurretEventDefOf.Warmup].ExecuteEvents();
			burstsTillWarmup = CurrentFireMode.burstsTillWarmup;
		}

		protected void ValidateLockStatus()
		{
			if (vehicle != null)
			{
				if (!cannonTarget.IsValid && TurretTargeter.Turret != this && !vehicle.Deploying)
				{
					float angleDifference = vehicle.Angle - parentAngleCached;
					if (attachedTo is null)
					{
						rotation += 90 * (vehicle.Rotation.AsInt - parentRotCached.AsInt) + angleDifference;
					}
					TurretRotationTargeted = rotation;
				}
				parentRotCached = vehicle.Rotation;
				parentAngleCached = vehicle.Angle;
			}
		}

		public virtual string GetUniqueLoadID()
		{
			return "VehicleTurretGroup_" + uniqueID;
		}

		public override string ToString()
		{
			return $"{turretDef}_{GetUniqueLoadID()}";
		}

		public bool ContainsAmmoDefOrShell(ThingDef def)
		{
			ThingDef projectile = null;
			if (def.projectileWhenLoaded != null)
			{
				projectile = def.projectileWhenLoaded;
			}
			return turretDef.ammunition.Allows(def) || turretDef.ammunition.Allows(projectile);
		}

		public virtual IEnumerable<string> ConfigErrors(VehicleDef vehicleDef)
		{
			if (turretDef is null)
			{
				yield return $"<field>turretDef</field> is a required field for <type>VehicleTurret</type>.".ConvertRichText();
			}
			if (string.IsNullOrEmpty(key))
			{
				yield return "<field>key</field> must be included for each <type>VehicleTurret</type>".ConvertRichText();
			}
			if (vehicleDef.GetCompProperties<CompProperties_VehicleTurrets>().turrets.Select(x => x.key).GroupBy(y => y).Where(y => y.Count() > 1).Select(z => z.Key).NotNullAndAny())
			{
				yield return $"Duplicate turret key {key}";
			}
		}

		public static string TurretEnableTypeDisableReason(TurretDisableType currentType)
		{
			return currentType switch
			{
				TurretDisableType.InFlight => "TurretDisableType_Always".Translate().ToString(),
				TurretDisableType.Strafing => "TurretDisableType_Always".Translate().ToString(),
				TurretDisableType.Grounded => "TurretDisableType_Always".Translate().ToString(),
				_ => "TurretDisableType_Always".Translate().ToString(),
			};
		}

		public virtual void OnDestroy()
		{
			RGBMaterialPool.Release(this);
			if (!turretGraphics.NullOrEmpty())
			{
				foreach (TurretDrawData turretDrawData in turretGraphics)
				{
					RGBMaterialPool.Release(turretDrawData);
				}
			}
		}

		public virtual void ExposeData()
		{
			Scribe_Values.Look(ref autoTargetingActive, nameof(autoTargetingActive));

			Scribe_Values.Look(ref reloadTicks, nameof(reloadTicks));
			Scribe_Values.Look(ref burstTicks, nameof(burstTicks));

			Scribe_Values.Look(ref uniqueID, nameof(uniqueID), defaultValue: -1);
			Scribe_Values.Look(ref key, nameof(key));
			Scribe_Values.Look(ref upgradeKey, nameof(upgradeKey), forceSave: true);

			Scribe_Defs.Look(ref turretDef, nameof(turretDef));

			Scribe_Values.Look(ref targetPersists, nameof(targetPersists));
			Scribe_Values.Look(ref autoTargeting, nameof(autoTargeting));
			Scribe_Values.Look(ref manualTargeting, nameof(manualTargeting));

			Scribe_Values.Look(ref queuedToFire, nameof(queuedToFire));
			Scribe_Values.Look(ref currentFireMode, nameof(currentFireMode));
			Scribe_Values.Look(ref currentHeatRate, nameof(currentHeatRate));
			Scribe_Values.Look(ref triggeredCooldown, nameof(triggeredCooldown));
			Scribe_Values.Look(ref ticksSinceLastShot, nameof(ticksSinceLastShot));
			Scribe_Values.Look(ref burstsTillWarmup, nameof(burstsTillWarmup));

			Scribe_Values.Look(ref rotation, nameof(rotation), defaultValue: defaultAngleRotated);
			Scribe_Values.Look(ref restrictedTheta, nameof(restrictedTheta), defaultValue: (int)Mathf.Abs(angleRestricted.x - (angleRestricted.y + 360)).ClampAngle());

			Scribe_Defs.Look(ref loadedAmmo, nameof(loadedAmmo));
			Scribe_Defs.Look(ref savedAmmoType, nameof(savedAmmoType));
			Scribe_Values.Look(ref shellCount, nameof(shellCount));
			Scribe_Values.Look(ref gizmoLabel, nameof(gizmoLabel));

			Scribe_TargetInfo.Look(ref cannonTarget, nameof(cannonTarget), defaultValue: LocalTargetInfo.Invalid);

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				parentRotCached = vehicle.Rotation;
				parentAngleCached = vehicle.Angle;
				if (cannonTarget.IsValid)
				{
					AlignToTargetRestricted(); //reassigns rotationTargeted for turrets currently turning
				}
				InitRecoilTrackers();
			}
		}

		public static SubGizmo SubGizmo_RemoveAmmo(VehicleTurret turret)
		{
			return new SubGizmo
			{
				drawGizmo = delegate (Rect rect)
				{
					//Widgets.DrawTextureFitted(rect, BGTex, 1);
					if (turret.loadedAmmo != null)
					{
						GUIState.Push();
						{
							GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, turret.CannonIconAlphaTicked); //Only modify alpha
							Widgets.DrawTextureFitted(rect, turret.loadedAmmo.uiIcon, 1);
							
							GUIState.Reset();

							Rect ammoCountRect = new Rect(rect);
							string ammoCount = turret.vehicle.inventory.innerContainer.Where(td => td.def == turret.loadedAmmo).Select(t => t.stackCount).Sum().ToStringSafe();
							ammoCountRect.y += ammoCountRect.height / 2;
							ammoCountRect.x += ammoCountRect.width - Text.CalcSize(ammoCount).x;
							Widgets.Label(ammoCountRect, ammoCount);
						}
						GUIState.Pop();
					}
					else if (turret.turretDef.genericAmmo && turret.turretDef.ammunition.AllowedDefCount > 0)
					{
						Widgets.DrawTextureFitted(rect, turret.turretDef.ammunition.AllowedThingDefs.FirstOrDefault().uiIcon, 1);
						
						Rect ammoCountRect = new Rect(rect);
						string ammoCount = turret.vehicle.inventory.innerContainer.Where(td => td.def == turret.turretDef.ammunition.AllowedThingDefs.FirstOrDefault()).Select(t => t.stackCount).Sum().ToStringSafe();
						ammoCountRect.y += ammoCountRect.height / 2;
						ammoCountRect.x += ammoCountRect.width - Text.CalcSize(ammoCount).x;
						Widgets.Label(ammoCountRect, ammoCount);
					}
				},
				canClick = delegate ()
				{
					return turret.shellCount > 0;
				},
				onClick = delegate ()
				{
					turret.TryRemoveShell();
					SoundDefOf.Artillery_ShellLoaded.PlayOneShot(new TargetInfo(turret.vehicle.Position, turret.vehicle.Map, false));
				},
				tooltip = turret.loadedAmmo?.LabelCap
			};
		}

		public static SubGizmo SubGizmo_ReloadFromInventory(VehicleTurret turret)
		{
			return new SubGizmo
			{
				drawGizmo = delegate (Rect rect)
				{
					Widgets.DrawTextureFitted(rect, VehicleTex.ReloadIcon, 1);
				},
				canClick = delegate ()
				{
					return true;
				},
				onClick = delegate ()
				{
					if (turret.turretDef.ammunition is null)
					{
						turret.ReloadCannon();
					}
					else if (turret.turretDef.genericAmmo)
					{
						if (!turret.vehicle.inventory.innerContainer.Contains(turret.turretDef.ammunition.AllowedThingDefs.FirstOrDefault()))
						{
							Messages.Message("VF_NoAmmoAvailable".Translate(), MessageTypeDefOf.RejectInput);
						}
						else
						{
							turret.ReloadCannon(turret.turretDef.ammunition.AllowedThingDefs.FirstOrDefault());
						}
					}
					else
					{
						List<FloatMenuOption> options = new List<FloatMenuOption>();
						var ammoAvailable = turret.vehicle.inventory.innerContainer.Where(d => turret.ContainsAmmoDefOrShell(d.def)).Select(t => t.def).Distinct().ToList();
						for (int i = ammoAvailable.Count - 1; i >= 0; i--)
						{
							ThingDef ammo = ammoAvailable[i];
							options.Add(new FloatMenuOption(ammoAvailable[i].LabelCap, delegate ()
							{
								turret.ReloadCannon(ammo, ammo != turret.savedAmmoType);
							}));
						}
						if (options.NullOrEmpty())
						{
							FloatMenuOption noAmmoOption = new FloatMenuOption("VF_VehicleTurrets_NoAmmoToReload".Translate(), null);
							noAmmoOption.Disabled = true;
							options.Add(noAmmoOption);
						}
						Find.WindowStack.Add(new FloatMenu(options));
					}
				},
				tooltip = "VF_ReloadVehicleTurret".Translate()
			};
		}

		public static SubGizmo SubGizmo_FireMode(VehicleTurret turret)
		{
			return new SubGizmo
			{
				drawGizmo = delegate (Rect rect)
				{
					Widgets.DrawTextureFitted(rect, turret.CurrentFireMode.Icon, 1);
				},
				canClick = delegate ()
				{
					return turret.turretDef.fireModes.Count > 1;
				},
				onClick = delegate ()
				{
					turret.CycleFireMode();
				},
				tooltip = turret.CurrentFireMode.label
			};
		}

		public static SubGizmo SubGizmo_AutoTarget(VehicleTurret turret)
		{
			return new SubGizmo
			{
				drawGizmo = delegate (Rect rect)
				{
					Widgets.DrawTextureFitted(rect, VehicleTex.AutoTargetIcon, 1);
					Rect checkboxRect = new Rect(rect.x + rect.width / 2, rect.y + rect.height / 2, rect.width / 2, rect.height / 2);
					GUI.DrawTexture(checkboxRect, turret.AutoTarget ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex);
				},
				canClick = delegate ()
				{
					return turret.CanAutoTarget;
				},
				onClick = delegate ()
				{
					turret.SwitchAutoTarget();
				},
				tooltip = "VF_AutoTargeting".Translate(turret.AutoTarget.ToStringYesNo())
			};
		}

		[NoProfiling]
		public (Texture2D mainTex, Texture2D maskTex) GetTextures(Rot8 rot)
		{
			throw new NotImplementedException();
		}

		public struct SubGizmo
		{
			public Action<Rect> drawGizmo;
			public Func<bool> canClick;
			public Action onClick;
			public string tooltip;

			public bool IsValid => onClick != null;

			public static SubGizmo None { get; private set; } = new SubGizmo();
		}

		public class TurretDrawData : IMaterialCacheTarget
		{
			public VehicleTurret turret;

			public Graphic_Turret graphic;
			public GraphicDataRGB graphicDataRGB;
			public VehicleTurretRenderData renderData;

			public TurretDrawData(VehicleTurret turret, VehicleTurretRenderData renderData)
			{
				this.turret = turret;
				this.renderData = renderData;
			}

			public int MaterialCount => 1;

			public PatternDef PatternDef => turret.PatternDef;

			public string Name => $"{turret.turretDef}_{turret.key}_{turret.vehicle?.ThingID ?? "Def"}";

			public void Set(GraphicDataRGB copyFrom, PatternData patternData)
			{
				graphic = GenerateGraphicData(this, turret, copyFrom, patternData, ref graphicDataRGB);
			}

			public Vector3 DrawOffset(Vector3 drawPos, Rot8 rot)
			{
				float locationRotation = 0f;
				if (turret.attachedTo != null)
				{
					locationRotation = TurretRotationFor(rot, turret.attachedTo.TurretRotation);
				}
				Vector3 graphicOffset = graphic.DrawOffset(rot);
				Vector2 rotatedPoint = Ext_Math.RotatePointClockwise(graphicOffset.x, graphicOffset.z, locationRotation);
				return new Vector3(drawPos.x + rotatedPoint.x, drawPos.y + graphicOffset.y, drawPos.z + rotatedPoint.y);
			}

			public override string ToString()
			{
				return $"TurretDrawData_{turret.key}_({graphicDataRGB.texPath})";
			}
		}
	}
}
