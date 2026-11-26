const webpackMerge = require("webpack-merge");

const baseWebpackConfig = require("@kentico/xperience-webpack-config");

// Kentico's webpack configuration for administration modules: it leaves react, react-router-dom and
// Kentico's own packages to the administration (they're shared at runtime, never bundled).
module.exports = (opts, argv) => {
  const baseConfig = (webpackConfigEnv, argv) =>
    baseWebpackConfig({
      // Must match RegisterClientModule(OrgName, ProjectName) in ContentSyncToolkitAdminModule.cs and
      // AdminOrgName / AdminClientPath in the .csproj.
      orgName: "xperiencecommunity",
      projectName: "content-sync-toolkit",
      webpackConfigEnv: webpackConfigEnv,
      argv: argv,
    });

  const projectConfig = {
    module: {
      rules: [
        {
          test: /\.(js|ts)x?$/,
          exclude: [/node_modules/],
          loader: "babel-loader",
        },
      ],
    },
    output: {
      clean: true,
    },
  };

  return webpackMerge.merge(projectConfig, baseConfig(opts, argv));
};
