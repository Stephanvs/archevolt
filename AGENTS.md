## Documentation site

The `docs/` folder is a [Blume](https://useblume.dev) site. The copy in the docs site is targeted towards consumers of the library, not the authors.

To run the docs site;

```bash
pnpm install
pnpm dev
pnpm build
```

`pnpm dev` serves the docs with hot reload. `pnpm build` writes static HTML to `dist/`.

### Landing page

The site root (`/`) is a custom Astro page, `pages/index.astro`, with its pieces in `pages/_home/`. Blume mounts the docs under `/docs` through `basePath` in `blume.config.ts`.

- In `docs/`, write links as if the docs were mounted at the root (`/guides/queries`). Blume adds `/docs`.
- In `pages/`, Blume does not rewrite links, so write the full path (`/docs/guides/queries`).
- `public/_redirects` sends the URLs from before the move to their `/docs` locations. Blume prefixes every configured redirect with `basePath`, so these rules cannot live in `blume.config.ts`. Cloudflare rejects the deploy when the file holds more than 100 rules.
