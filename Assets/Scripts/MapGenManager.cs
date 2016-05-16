using System;
using System.Collections.Generic;
using ActionStreetMap.Core;
using ActionStreetMap.Core.Geometry;
using ActionStreetMap.Core.Tiling;
using ActionStreetMap.Core.Utils;
using ActionStreetMap.Explorer;
using ActionStreetMap.Explorer.Infrastructure;
using ActionStreetMap.Infrastructure.Bootstrap;
using ActionStreetMap.Infrastructure.Config;
using ActionStreetMap.Infrastructure.Dependencies;
using ActionStreetMap.Infrastructure.Diagnostic;
using ActionStreetMap.Infrastructure.IO;
using ActionStreetMap.Infrastructure.Reactive;
using ActionStreetMap.Maps.GeoCoding;
using ActionStreetMap.Unity.IO;
using UnityEngine;
using UnityEngine.Assertions;

using Component = ActionStreetMap.Infrastructure.Dependencies.Component;
using RenderMode = ActionStreetMap.Core.RenderMode;

namespace MapGen {
    public class MapGenManager : MonoBehaviour {
        /**
         * Public properties.
         */

        /** Central point (centre of York). */
        public double CentreLatitude = 53.9590555;
        public double CentreLongitude = -1.0815361;

        /** Tile size (metres). */
        public float TileSize = 500;

        /** Total world size (tiles). */
        public Vector2 WorldSize = new Vector2(3, 3);

        /** High detail world size (tiles). */
        public Vector2 DetailedWorldSize = new Vector2(3, 3);

        /** Material to use for the combined objects. */
        public Material CombinedMaterial;

        /** Whether to enable mesh reduction. */
        public bool EnableMeshReduction = true;

        /** Whether to enable asset exporting. */
        public bool EnableExport = true;

        /** Whether to filter out info nodes. */
        public bool FilterInfoNodes = true;

        /** Whether to add physics colliders. */
        public bool AddColliders = true;

        public GeoCoordinate Centre { get; private set; }
        public IConfigSection Config { get; private set; }
        public bool IsInitialized { get; private set; }

        /**
         * Internals.
         */

        /** Current position. */
        private Vector3 m_position = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        /** ActionStreetMap systems. */
        private IContainer m_container;
        private IMessageBus m_messageBus;
        private UnityTrace m_trace;
        private GameRunner m_gameRunner;
        private ITileController m_tileController;

        void Awake() {
            Assert.raiseExceptions = true;

            IsInitialized = false;
            Centre = new GeoCoordinate(CentreLatitude, CentreLongitude);

            Scheduler.MainThread = UnityMainThreadScheduler.MainThread;

            m_container = new Container();
            m_messageBus = new MessageBus();
            m_trace = new UnityTrace();

            UnityMainThreadDispatcher.RegisterUnhandledExceptionCallback(
                ex => m_trace.Error("Fatal", ex, "Unhandled exception"));

            m_container.RegisterInstance(this);
            m_container.RegisterInstance(new MapGenTileExporter(this));
            m_container.RegisterInstance<ITrace>(m_trace);
            m_container.RegisterInstance<IPathResolver>(new PathResolver());
            m_container.RegisterInstance(m_messageBus);
            m_container.Register(Component.For<IFileSystemService>().Use<FileSystemService>().Singleton());

            Config = ConfigBuilder.GetDefault()
                .SetTileSettings(TileSize, 40)
                .SetRenderOptions(
                    RenderMode.Scene,
                    new Rectangle2d(0, 0, TileSize, TileSize))
                .Build();

            m_gameRunner = new GameRunner(m_container, Config);
            m_gameRunner.RegisterPlugin<MapGenBootstrapper>("mapgen", this);
            m_gameRunner.Bootstrap();
        }

        void OnEnable() {
            Observable.Start(
                () => {
                    m_tileController = GetService<ITileController>();
                    m_gameRunner.RunGame(Centre);
                    IsInitialized = true;
                },
                Scheduler.ThreadPool);
        }

        void Update() {
            if (IsInitialized && m_position != transform.position) {
                m_position = transform.position;
                Scheduler.ThreadPool.Schedule(
                    () => m_tileController.OnNext(new Vector2d(m_position.x, m_position.z)));
            }
        }

        void OnValidate() {
            if (WorldSize.x <= 0)
                WorldSize.x = 1;
            WorldSize.x = ((WorldSize.x % 2) != 0) ? WorldSize.x : WorldSize.x - 1;

            if (WorldSize.y <= 0)
                WorldSize.y = 1;
            WorldSize.y = ((WorldSize.y % 2) != 0) ? WorldSize.y : WorldSize.y - 1;

            if (DetailedWorldSize.x > WorldSize.x) {
                DetailedWorldSize.x = WorldSize.x;
            } else if (DetailedWorldSize.x > 0) {
                DetailedWorldSize.x = ((DetailedWorldSize.x % 2) != 0) ? DetailedWorldSize.x : DetailedWorldSize.x - 1;
            } else {
                DetailedWorldSize.x = 0;
            }

            if (DetailedWorldSize.y > WorldSize.y) {
                DetailedWorldSize.y = WorldSize.y;
            } else if (DetailedWorldSize.y > 0) {
                DetailedWorldSize.y = ((DetailedWorldSize.y % 2) != 0) ? DetailedWorldSize.y : DetailedWorldSize.y - 1;
            } else {
                DetailedWorldSize.y = 0;
            }
        }

        public T GetService<T>() {
            return m_container.Resolve<T>();
        }
    }
}