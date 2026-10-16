import { posix } from 'node:path'

export const repository = 'https://github.com/Almis90/native-channels'

// These pages include the original Markdown so the website and repository stay in sync.
export const includedPages = {
  'pdf-viewer.md': 'plugins/Plugin.Maui.PdfViewer/README.md',
  'pdf-sample.md': 'samples/PdfViewer.Sample/README.md',
  'pdf-verification.md': 'plugins/Plugin.Maui.PdfViewer/VERIFICATION.md',
  'volume-sample.md': 'samples/VolumeMonitor.Sample/README.md',
  'volume-verification.md': 'plugins/Plugin.Maui.VolumeMonitor/VERIFICATION.md',
  'volume-monitor.md': 'plugins/Plugin.Maui.VolumeMonitor/README.md',
  'audio-streamer.md': 'plugins/Plugin.Maui.AudioStreamer/README.md',
  'audio-verification.md': 'plugins/Plugin.Maui.AudioStreamer/VERIFICATION.md',
  'audio-sample.md': 'samples/AudioStreamer.Sample/README.md',
  'currency.md': 'plugins/Plugin.Maui.Currency/README.md',
  'currency-verification.md': 'plugins/Plugin.Maui.Currency/VERIFICATION.md',
  'sample.md': 'samples/NativeChannels.Sample/README.md',
  'currency-sample.md': 'samples/Currency.Sample/README.md',
  'currency-avalonia.md': 'samples/Currency.Avalonia.Android/README.md',
  'changelog.md': 'CHANGELOG.md',
  'contributing.md': 'CONTRIBUTING.md',
}

export function sourcePath(page) {
  return includedPages[page] ?? `docs/${page}`
}

function websiteLink(href, page) {
  if (!href || href.startsWith('#') || href.startsWith('/')) return href
  const githubPrefix = `${repository}/blob/main/`
  let target
  if (href.startsWith(githubPrefix)) {
    target = href.slice(githubPrefix.length)
  } else if (/^[a-z][a-z\d+.-]*:/i.test(href)) {
    return href
  } else {
    target = posix.normalize(posix.join(posix.dirname(sourcePath(page)), href))
  }
  const [file, fragment] = target.split('#')
  const suffix = fragment ? `#${fragment}` : ''
  const includedPage = Object.entries(includedPages).find(([, source]) => source === file)?.[0]
  if (includedPage) return `/${includedPage}${suffix}`
  if (file.startsWith('docs/') && file.endsWith('.md')) return `/${file.slice(5)}${suffix}`
  return `${githubPrefix}${target}`
}

export function repositoryLinks(md) {
  md.core.ruler.after('inline', 'repository-links', (state) => {
    for (const token of state.tokens) {
      for (const child of token.children ?? []) {
        if (child.type === 'link_open') {
          child.attrSet('href', websiteLink(child.attrGet('href'), state.env.relativePath))
        }
      }
    }
  })
}
