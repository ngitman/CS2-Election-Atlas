const path = require("path");
const webpack = require("webpack");
const mod = require("./mod.json");

// The game reads this header to register the UI module's id, author, version and dependencies
// (without it the module registers with null metadata). Same fields as the toolchain's UI template.
const header = [
  "/**",
  " * Cities: Skylines II UI Module",
  ` * Id: ${mod.id}`,
  ` * Author: ${mod.author}`,
  ` * Version: ${mod.version}`,
  ` * Dependencies: ${mod.dependencies.join(",")}`,
  " */",
].join("\n");

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
  plugins: [
    // Added after minification (REPORT stage) so the minifier can't strip it.
    new webpack.BannerPlugin({ banner: header, raw: true, stage: webpack.Compilation.PROCESS_ASSETS_STAGE_REPORT }),
  ],
  devtool: false,
  performance: { hints: false },
};
