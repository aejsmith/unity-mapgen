using ActionStreetMap.Infrastructure.IO;

namespace MapGen {
    class PathResolver : IPathResolver {
        public string Resolve(string path) {
            if (path.StartsWith("Config") || path.StartsWith("Maps"))
                path = "Assets//Map//Resources//" + path;

            return path;
        }
    }
}
