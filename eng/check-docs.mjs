import assert from 'node:assert/strict'
import { existsSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs'
import { join, relative, resolve, sep } from 'node:path'

const output = resolve('docs/.vitepress/dist')
const origin = 'https://almis90.github.io'
const base = '/native-channels/'

function* files(directory) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) yield* files(path)
    else yield path
  }
}

function localFile(pathname) {
  assert(pathname.startsWith(base), `Link escapes the Pages base path: ${pathname}`)
  const path = resolve(output, decodeURIComponent(pathname.slice(base.length)))
  assert(path === output || path.startsWith(output + sep), `Invalid site path: ${pathname}`)
  return [path, path + '.html', join(path, 'index.html')]
    .find(candidate => existsSync(candidate) && statSync(candidate).isFile())
}

let pages = 0
let links = 0
for (const path of files(output)) {
  if (!path.endsWith('.html')) continue
  pages++
  const html = readFileSync(path, 'utf8')
  const pageUrl = origin + base + relative(output, path).split(sep).join('/')
  for (const [, raw] of html.matchAll(/\b(?:href|src)="([^"]+)"/g)) {
    const url = new URL(raw.replaceAll('&amp;', '&'), pageUrl)
    if (url.origin !== origin) continue
    if (/^https?:/.test(raw) && !url.pathname.startsWith(base)) continue
    const target = localFile(url.pathname)
    assert(target, `${path}: missing ${url.pathname}`)
    if (url.hash && target.endsWith('.html')) {
      const id = decodeURIComponent(url.hash.slice(1))
      assert(readFileSync(target, 'utf8').includes(`id="${id}"`), `${path}: missing ${url.pathname}${url.hash}`)
    }
    links++
  }
}
assert(pages >= 30, `Expected the full documentation set, found ${pages} pages`)
assert(existsSync(join(output, 'sitemap.xml')), 'Missing sitemap')
writeFileSync(join(output, '.nojekyll'), '')
console.log(`PASS ${pages} pages: ${links} local links, fragments, and assets checked`)
