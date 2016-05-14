using System;
using System.Diagnostics;
using ActionStreetMap.Core;
using ActionStreetMap.Core.Geometry;
using ActionStreetMap.Core.Tiling;
using ActionStreetMap.Core.Tiling.Models;
using ActionStreetMap.Infrastructure.Diagnostic;
using ActionStreetMap.Infrastructure.Reactive;
using UnityEngine;

using Debug = UnityEngine.Debug;

namespace MapGen {
    public class MapGenTileListener {
        private readonly MapGenManager m_manager;

        public MapGenTileListener(MapGenManager manager) {
            m_manager = manager;

            IMessageBus messageBus = m_manager.GetService<IMessageBus>();
            messageBus.AsObservable<TileLoadFinishMessage>().Do(m => OnTileBuildFinished(m.Tile)).Subscribe();
        }

        private void OnTileBuildFinished(Tile tile) {
            Observable.Start(
                () => m_manager.GetService<MapGenTileExporter>().Export(tile),
                Scheduler.MainThread).Wait();
        }
    }
}
