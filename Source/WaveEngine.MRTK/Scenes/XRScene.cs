﻿// Copyright © Wave Engine S.L. All rights reserved. Use is subject to license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using WaveEngine.Bullet;
using WaveEngine.Common.Graphics;
using WaveEngine.Common.Input.Keyboard;
using WaveEngine.Components.Graphics3D;
using WaveEngine.Components.Primitives;
using WaveEngine.Components.XR;
using WaveEngine.Framework;
using WaveEngine.Framework.Graphics;
using WaveEngine.Framework.Physics3D;
using WaveEngine.Framework.Services;
using WaveEngine.Framework.XR;
using WaveEngine.Mathematics;
using WaveEngine.MRTK.Behaviors;
using WaveEngine.MRTK.Emulation;
using WaveEngine.MRTK.Services.InputSystem;

namespace WaveEngine.MRTK.Scenes
{
    /// <summary>
    /// Base class for scenes using XR.
    /// </summary>
    public abstract class XRScene : Scene
    {
        /// <summary>
        /// Gets cursors material Guid for released state.
        /// </summary>
        protected abstract Guid CursorMatReleased { get; }

        /// <summary>
        /// Gets cursors material Guid for pressed state.
        /// </summary>
        protected abstract Guid CursorMatPressed { get; }

        /// <summary>
        /// Gets holo hands material Guid.
        /// </summary>
        protected abstract Guid HoloHandsMat { get; }

        /// <summary>
        /// Gets spatial mapping material Guid.
        /// </summary>
        protected abstract Guid SpatialMappingMat { get; }

        /// <summary>
        /// Gets the texture for the hand rays.
        /// </summary>
        protected abstract Guid HandRayTexture { get; }

        /// <summary>
        /// Gets the Sampler for the hand rays.
        /// </summary>
        protected abstract Guid HandRaySampler { get; }

        /// <summary>
        /// Gets or sets the <see cref="CollisionCategory3D"/> used by the <see cref="Cursor"/> entities.
        /// Default: <see cref="CollisionCategory3D.Cat2"/>.
        /// </summary>
        public virtual CollisionCategory3D CursorCollisionCategory { get; protected set; } = CollisionCategory3D.Cat2;

        /// <summary>
        /// Gets or sets the <see cref="CollisionCategory3D"/> mask used by the <see cref="Cursor"/> entities.
        /// Default: <see cref="CollisionCategory3D.All"/>.
        /// <para>It indicates with which <see cref="CollisionCategory3D"/> the cursors will collide.</para>
        /// </summary>
        public virtual CollisionCategory3D CursorCollisionCategoryMask { get; protected set; } = CollisionCategory3D.All;

        /// <inheritdoc/>
        public override void RegisterManagers()
        {
            base.RegisterManagers();

            this.Managers.AddManager(new BulletPhysicManager3D());
            this.Managers.AddManager(new FocusProvider());
            this.Managers.AddManager(new VoiceCommandsProvider());
        }

        /// <inheritdoc/>
        protected override void CreateScene()
        {
            var forwardRenderPass = this.Managers.RenderManager.RenderPipeline.DefaultRenderPath as ForwardRenderPath;
            if (forwardRenderPass != null)
            {
                forwardRenderPass.ZPrePassIsEnabled = false;
            }

            var skyboxEntities = this.Managers.EntityManager.FindAllByTag(tag: "Skybox").ToArray();
            foreach (var entity in skyboxEntities)
            {
                this.Managers.EntityManager.Remove(entity);
            }

            this.InitHoloScene();
        }

        /// <inheritdoc/>
        protected override void Start()
        {
            base.Start();

            // Create GazeProvider
            Camera3D cam = this.Managers.EntityManager.FindFirstComponentOfType<Camera3D>();
            cam.Owner.AddComponent(new GazeProvider() { CollisionCategoryMask = this.CursorCollisionCategoryMask & ~this.CursorCollisionCategory });
        }

        private void CreateXRHandMesh(XRHandedness handedness, Material material)
        {
            Entity handEntity = new Entity()
                .AddComponent(new Transform3D())
                .AddComponent(new MaterialComponent() { Material = material })
                .AddComponent(new XRDeviceMeshComponent()
                {
                    XRModelName = handedness.ToString(),
                })
                .AddComponent(new MeshRenderer())
                .AddComponent(new TrackModelNode()
                {
                    SelectionStrategy = TrackModelNode.SelectionNodeStrategy.RootNode,
                })
                .AddComponent(new HoloHandsUpdater() { Handedness = handedness })
                ;

            this.Managers.EntityManager.Add(handEntity);
        }

        private Entity CreateCursor(XRHandedness handedness, Material pressedMaterial, Material releasedMaterial, Texture handRayTexture, SamplerState handRaySampler)
        {
            const float cursorPlaneSize = 0.01f;

            var mainCursor = new Entity($"{nameof(CursorTouch)}_{handedness}")
                .AddComponent(new Transform3D())
                .AddComponent(new CursorTouch()
                {
                    PressedMaterial = pressedMaterial,
                    ReleasedMaterial = releasedMaterial,
                })
                .AddComponent(new SphereCollider3D() { Radius = 0.005f })
                .AddComponent(new StaticBody3D()
                {
                    IsSensor = true,
                    CollisionCategories = this.CursorCollisionCategory,
                    MaskBits = this.CursorCollisionCategoryMask & ~this.CursorCollisionCategory,
                })
                .AddChild(new Entity("visual")
                    .AddComponent(new Transform3D())
                    .AddComponent(new MaterialComponent())
                    .AddComponent(new PlaneMesh()
                    {
                        TwoSides = true,
                        PlaneNormal = PlaneMesh.NormalAxis.ZNegative,
                        Width = cursorPlaneSize,
                        Height = cursorPlaneSize,
                    })
                    .AddComponent(new MeshRenderer())
                    .AddComponent(new ProximityLight()));

            if (Application.Current.Container.IsRegistered<XRPlatform>())
            {
                // HoloLens 2
                mainCursor.AddComponent(new TrackXRJoint()
                {
                    Handedness = handedness,
                    SelectionStrategy = TrackXRDevice.SelectionDeviceStrategy.ByHandedness,
                    JointKind = XRHandJointKind.IndexTip,
                    TrackingLostMode = TrackXRDevice.XRTrackingLostMode.KeepLastPose,
                })
                          .AddComponent(new HoloLensControlBehavior());
            }
            else
            {
                // Windows
                var key = handedness == XRHandedness.RightHand ? Keys.LeftShift : Keys.Space;
                mainCursor.AddComponent(new MouseControlBehavior() { Key = key });
            }

            var ray = new Entity()
                .AddComponent(new Transform3D())
                .AddComponent(new LineMesh()
                {
                    IsCameraAligned = true,
                    LinePoints = new List<LinePointInfo>()
                    {
                        new LinePointInfo() { Position = Vector3.Zero, Thickness = 0.003f, Color = Color.White },
                        new LinePointInfo() { Position = -Vector3.UnitZ, Thickness = 0.003f, Color = Color.White },
                    },
                    DiffuseTexture = handRayTexture,
                    DiffuseSampler = handRaySampler,
                })
                .AddComponent(new LineMeshRenderer3D());

            var farCursor = new Entity($"{nameof(CursorRay)}_{handedness}")
                .AddComponent(new Transform3D())
                .AddComponent(new CursorRay()
                {
                    PressedMaterial = pressedMaterial,
                    ReleasedMaterial = releasedMaterial,
                    TouchCursorEntity = mainCursor,
                })
                .AddChild(ray)
                .AddChild(new Entity("visual")
                    .AddComponent(new Transform3D())
                    .AddComponent(new MaterialComponent())
                    .AddComponent(new PlaneMesh()
                    {
                        PlaneNormal = PlaneMesh.NormalAxis.ZNegative,
                        Width = cursorPlaneSize,
                        Height = cursorPlaneSize,
                    })
                    .AddComponent(new MeshRenderer())
                    .AddComponent(new CameraDistanceScale() { UpdateOrder = 1 })
                    .AddComponent(new HoverLight()));

            var entityManager = this.Managers.EntityManager;
            entityManager.Add(mainCursor);
            entityManager.Add(farCursor);

            return mainCursor;
        }

        /// <summary>
        /// Initializes scene for HoloLens.
        /// </summary>
        protected void InitHoloScene()
        {
            var assetsManager = this.Managers.AssetSceneManager;

            // Create cursors
            var cursorMatPressed = assetsManager.Load<Material>(this.CursorMatPressed);
            var cursorMatReleased = assetsManager.Load<Material>(this.CursorMatReleased);
            var handRayTexture = assetsManager.Load<Texture>(this.HandRayTexture);
            var handRaySampler = assetsManager.Load<SamplerState>(this.HandRaySampler);
            this.CreateCursor(XRHandedness.LeftHand, cursorMatPressed, cursorMatReleased, handRayTexture, handRaySampler);
            this.CreateCursor(XRHandedness.RightHand, cursorMatPressed, cursorMatReleased, handRayTexture, handRaySampler);

            // Create hand meshes
            var handMat = this.HoloHandsMat == Guid.Empty ? null : assetsManager.Load<Material>(this.HoloHandsMat);
            if (handMat != null)
            {
                this.CreateXRHandMesh(XRHandedness.LeftHand, handMat);
                this.CreateXRHandMesh(XRHandedness.RightHand, handMat);
            }

            var entityManager = this.Managers.EntityManager;

            // Create cursor position updater
            if (entityManager.FindFirstComponentOfType<CursorPosShaderUpdater>() == null)
            {
                entityManager.Add(new Entity(nameof(CursorPosShaderUpdater))
                    .AddComponent(new CursorPosShaderUpdater()));
            }

            // Create Holographic batching disabler
            if (entityManager.FindFirstComponentOfType<HolographicBatching>() == null)
            {
                entityManager.Add(new Entity(nameof(HolographicBatching))
                    .AddComponent(new HolographicBatching()));
            }

            // Create spatial mapping
            var spatialMappingMat = this.SpatialMappingMat == Guid.Empty ? null : assetsManager.Load<Material>(this.SpatialMappingMat);
            entityManager.Add(new Entity(nameof(SpatialMapping))
                         .AddComponent(new SpatialMapping()
                         {
                             GenerateColliders = true,
                             UpdateInterval = TimeSpan.FromSeconds(4),
                             Material = spatialMappingMat,
                         }));
        }
    }
}