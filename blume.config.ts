import { defineConfig } from "blume";
import { filesystem, githubReleases } from "blume/sources";

export default defineConfig({
  title: "Archevolt",
  description:
    "A headless archetype ECS for .NET 11 and C# 15, with a source generator for typed queries, bundles, modules, and schedules.",
  content: {
    sources: [
      filesystem({ root: "docs" }),
      githubReleases({
        owner: "Stephanvs",
        repo: "archevolt",
        prefix: "changelog",
      }),
    ],
  },
  deployment: {
    site: "https://archevolt.dev",
  },
  navigation: {
    tabs: [
      { label: "Docs", path: "/" },
      { label: "Changelog", path: "/changelog" },
    ],
  },
  github: {
    owner: "Stephanvs",
    repo: "archevolt",
    branch: "main",
  },
  footer: {
    links: [
      { label: "NuGet", href: "https://www.nuget.org/packages/Archevolt" },
      { label: "Source", href: "https://github.com/Stephanvs/archevolt" },
    ],
  },
});
