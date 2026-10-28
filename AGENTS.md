## Documentation site

The `docs/` folder is a [Blume](https://useblume.dev) site. The copy in the docs site is targeted towards consumers of the library, not the authors.

To run the docs site;

```bash
pnpm install
pnpm dev
pnpm build
```

`pnpm dev` serves the docs with hot reload. `pnpm build` writes static HTML to `dist/`.
