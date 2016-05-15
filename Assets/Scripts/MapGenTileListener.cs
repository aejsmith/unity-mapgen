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
            messageBus.AsObservable<TileLoadFinishMessage>().Do(m => OnTileLoadFinish(m.Tile)).Subscribe();
            messageBus.AsObservable<WorldLoadFinishMessage>().Do(m => OnWorldLoadFinish()).Subscribe();
        }

        private void OnTileLoadFinish(Tile tile) {
            Observable.Start(
                () => m_manager.GetService<MapGenTileExporter>().ExportTile(tile),
                Scheduler.MainThread).Wait();
        }

        private void OnWorldLoadFinish() {
            Observable.Start(
                () => m_manager.GetService<MapGenTileExporter>().Finish(),
                Scheduler.MainThread).Wait();
        }
    }
}
