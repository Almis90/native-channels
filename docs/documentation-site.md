# Documentation site

The website uses VitePress with task-based navigation, local search, light/dark themes, source links,
and a C# API guide. README content introduces the project; the site holds setup instructions,
channel contracts, plugin authoring, reference examples, and verification evidence.

## Local preview

Use Node.js 22 or newer; CI uses Node.js 24. From the repository root:

```sh
npm ci
npm run docs:dev
```

Preview the production build under the same `/native-channels/` base path used by GitHub Pages:

```sh
npm run docs:build
npm run docs:preview
```

The build performs VitePress's Markdown link checks and then checks the generated HTML for broken
local links, fragments, and assets. Output lives in `docs/.vitepress/dist/`, which is ignored by Git.
Building or previewing locally does not publish anything.

## Edit content

- Guides live in `docs/`. Navigation and site metadata live in `docs/.vitepress/config.mts`.
- The landing page is `docs/index.md`; the complete guide index is `docs/overview.md`.
- `choosing-a-channel.md`, `examples.md`, and `plugin-authoring.md` are the main decision guides.
- Plugin docs, sample READMEs, the changelog, and contributor instructions are included from their
  original repository files. Edit those source files; the site's **Edit this page** links point there.
- `repository-links.mjs` maps links to published guides onto the website and preserves GitHub links
  for source files. It leaves the repository Markdown usable on GitHub.
- `api-reference.md` is a curated API guide. Interface snippets are included directly from C# source;
  it is not a generated member-by-member API catalog.

Search is built locally into the site. No external search account or hosted search service is required.

## Writing a guide

Start with the developer's task and state any required setup before a code example. Use the actual
API names and distinguish a complete example from application-supplied values. Link to detailed
contracts rather than repeating all timeout, serialization, or packaging rules on every page.

Keep implementation, compilation, and execution claims separate. Retain dated verification evidence;
keep introductory status text aligned with current capabilities. Include source snippets where the exact API
or package import matters, so those examples stay aligned with the implementation.

Before committing, run `npm run docs:build` and inspect the landing page and changed guides in a
browser. Check a narrow viewport and both themes, including navigation, tables, code blocks, and search.

## Deployment

The [documentation workflow](../.github/workflows/pages.yml) builds on pull requests and pushes to
`main`. Once the repository variable `PAGES_ENABLED` is `true`, pushes to `main` and manual runs on
`main` deploy to GitHub Pages. Pull requests build
and check the site without publishing it.

The repository's Pages source must be **GitHub Actions**. Enable `PAGES_ENABLED` only after
GitHub Pages is available for the repository. Keeping the flag unset allows builds to pass while
publication is unavailable or intentionally disabled. Deployments use the `github-pages`
environment, narrowly scoped Pages permissions, and a deployment queue that does not cancel an
in-progress publication. The website URL is `https://almis90.github.io/native-channels/`.

Documentation deployment does not publish NuGet packages, create releases, or move existing tags.
