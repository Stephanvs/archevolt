import { defineConfig } from "blume";

export default defineConfig({
  title: "Archevolt",
  description:
    "A headless archetype ECS for .NET 10 and C# 14, with a source generator for typed queries, bundles, modules, and schedules.",
  content: {
    root: "docs",
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
