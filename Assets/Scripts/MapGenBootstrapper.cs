using ActionStreetMap.Core;
using ActionStreetMap.Core.Tiling;
using ActionStreetMap.Explorer.Bootstrappers;
using ActionStreetMap.Explorer.Customization;
using ActionStreetMap.Infrastructure.Dependencies;
using ActionStreetMap.Infrastructure.Diagnostic;

namespace MapGen {
    public class MapGenBootstrapper : BootstrapperPlugin {
        private MapGenManager m_manager;

        public override string Name { get { return "mapgen"; } }

        [Dependency]
        public MapGenBootstrapper(MapGenManager manager) {
            m_manager = manager;
        }

        public override bool Run() {
            new MapGenTileListener(m_manager);

            CustomizationService
                .RegisterAtlas("main", TextureAtlasHelper.GetTextureAtlas());

            Container.Register(
                Component.For<ITileController>().Use<MapGenTileController>().Singleton());

            return true;
        }
    }
}
