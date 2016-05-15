using System;
using System.Linq;
using ActionStreetMap.Core;
using ActionStreetMap.Core.Geometry;
using ActionStreetMap.Core.Tiling;
using ActionStreetMap.Core.Tiling.Models;
using ActionStreetMap.Core.Utils;
using ActionStreetMap.Maps.Data;
using ActionStreetMap.Infrastructure.Config;
using ActionStreetMap.Infrastructure.Dependencies;
using ActionStreetMap.Infrastructure.Primitives;
using ActionStreetMap.Infrastructure.Reactive;
using ActionStreetMap.Infrastructure.Utilities;
using System.Reflection;

using Debug = UnityEngine.Debug;

namespace MapGen {
    sealed class WorldLoadFinishMessage {};

    class MapGenTileController : ITileController {
        private readonly object m_lockObj = new object();

        private GeoCoordinate m_currentPosition;
        private Vector2d m_currentMapPoint;
        private readonly MutableTuple<int, int> m_currentTileIndex = new MutableTuple<int, int>(0, 0);

        private readonly MapGenManager m_manager;
        private readonly ITileLoader m_tileLoader;
        private readonly IMessageBus m_messageBus;
        private readonly IObjectPool m_objectPool;
        private readonly IElementSourceProvider m_elementSourceProvider;

        private int m_endX;
        private int m_startX;
        private int m_endY;
        private int m_nextX;
        private int m_nextY;
        private bool m_loaded = false;

        [Dependency]
        public MapGenTileController(
            MapGenManager manager,
            ITileLoader tileLoader,
            ITileActivator tileActivator,
            IMessageBus messageBus,
            IObjectPool objectPool,
            IElementSourceProvider elementSourceProvider)
        {
            m_manager = manager;
            m_tileLoader = tileLoader;
            m_messageBus = messageBus;
            m_objectPool = objectPool;
            m_elementSourceProvider = elementSourceProvider;

            m_endX = (int)m_manager.WorldSize.x / 2;
            m_nextX = m_startX = -m_endX;
            m_endY = (int)m_manager.WorldSize.y / 2;
            m_nextY = -m_endY;
        }

        public Tile GetTile(Vector2d point) {
            return null;
        }

        public Tile CurrentTile { get { return null; } }

        public RenderMode Mode { get; set; }
        public Rectangle2d Viewport { get; set; }
        public double TileSize { get { return m_manager.TileSize; } }

        private void LoadNext() {
            Debug.Log("Load tile " + m_nextX + ", " + m_nextY);

            var tileCenter = new Vector2d(
                m_nextX * m_manager.TileSize,
                m_nextY * m_manager.TileSize);
            var tile = new Tile(
                m_manager.Centre,
                tileCenter,
                m_manager.WorldRenderMode,
                new Canvas(m_objectPool),
                m_manager.TileSize,
                m_manager.TileSize);

            m_messageBus.Send(new TileLoadStartMessage(tileCenter));
            m_tileLoader.Load(tile)
                .Subscribe(_ => { }, () => {
                    m_messageBus.Send(new TileLoadFinishMessage(tile));

                    /*
                     * Dodgy as hell hack to work around a bug without me having
                     * to install VS to recompile ASM. Basically there's some
                     * problem caching data which means if you load a tile then
                     * all adjacent tiles will fail to load properly because it
                     * will think the data is cached when it actually isn't.
                     * The Configure method on ElementSourceProvider will clear
                     * out its cache, however that class is internal to the
                     * assembly, so use reflection to get at it and call it.
                     */
                    Type type = m_elementSourceProvider.GetType();
                    MethodInfo methodInfo = type.GetMethod("Configure");
                    Debug.Assert(methodInfo != null);
                    IConfigSection config = m_manager.Config.GetSection(@"data/map");
                    methodInfo.Invoke(m_elementSourceProvider, new object[]{config});

                    if (++m_nextX > m_endX) {
                        m_nextX = m_startX;
                        m_nextY++;
                    }

                    if (m_nextY <= m_endY) {
                        LoadNext();
                    } else {
                        /* Notify the exporter that we have finished. */
                        m_messageBus.Send(new WorldLoadFinishMessage());
                    }
                });
        }

        Vector2d IPositionObserver<Vector2d>.CurrentPosition { get { return m_currentMapPoint; } }

        void IObserver<Vector2d>.OnNext(Vector2d value) {
            var geoPosition = GeoProjection.ToGeoCoordinate(m_manager.Centre, value);
            lock (m_lockObj) {
                m_currentMapPoint = value;
                m_currentPosition = geoPosition;
                m_currentTileIndex.Item1 = Convert.ToInt32(value.X / m_manager.TileSize);
                m_currentTileIndex.Item2 = Convert.ToInt32(value.Y / m_manager.TileSize);

                if (!m_loaded) {
                    Debug.Log("Loading\n");
                    LoadNext();
                    m_loaded = true;
                }
            }
        }

        void IObserver<Vector2d>.OnError(Exception error) {}
        void IObserver<Vector2d>.OnCompleted() {}

        GeoCoordinate IPositionObserver<GeoCoordinate>.CurrentPosition { get { return m_currentPosition; } }

        void IObserver<GeoCoordinate>.OnNext(GeoCoordinate value) {
            m_currentPosition = value;
            (this as IPositionObserver<Vector2d>).OnNext(
                GeoProjection.ToMapCoordinate(m_manager.Centre, value));
        }

        void IObserver<GeoCoordinate>.OnError(Exception error) {}
        void IObserver<GeoCoordinate>.OnCompleted() {}
    }
}
