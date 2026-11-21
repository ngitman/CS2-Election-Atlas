const path = require("path");
const mod = require("./mod.json");

// The toolchain sets CSII_USERDATAPATH; without it the build lands in ./dist.
const userData = process.env.CSII_USERDATAPATH;
const outDir = userData ? path.join(userData, "Mods", mod.id) : path.resolve(__dirname, "dist");

module.exports = {
  entry: { [mod.id]: path.join(__dirname, "src", "index.tsx") },
  output: {
    path: outDir,
    filename: "[name].mjs",
    library: { type: "module" },
    publicPath: "coui://ui-mods/", // as in the toolchain's UI template, for images and fonts
    clean: false,
  },
  experiments: { outputModule: true },
  resolve: { extensions: [".tsx", ".ts", ".js"] },
  module: {
    rules: [{ test: /\.tsx?$/, use: "ts-loader", exclude: /node_modules/ }],
  },
  // The game puts these on window (checked in Cities2_Data/Content/Game/UI/index.js).
  externalsType: "window",
  externals: {
    react: "React",
    "react-dom": "ReactDOM",
    "cs2/api": "cs2/api",
    "cs2/bindings": "cs2/bindings",
    "cs2/l10n": "cs2/l10n",
    "cs2/ui": "cs2/ui",
    "cs2/utils": "cs2/utils",
    "cs2/input": "cs2/input",
    "cs2/modding": "cs2/modding",
    "cohtml/cohtml": "cohtml/cohtml",
  },
  devtool: false,
  performance: { hints: false },
};
