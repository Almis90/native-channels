import { posix } from 'node:path'

export const repository = 'https://github.com/Almis90/native-channels'

// These pages include the original Markdown so the website and repository stay in sync.
export const includedPages = {
  'pdf-viewer.md': 'plugins/Plugin.Maui.PdfViewer/README.md',
  'pdf-sample.md': 'samples/Maui/PdfViewer/README.md',
  'pdf-verification.md': 'plugins/Plugin.Maui.PdfViewer/VERIFICATION.md',
  'volume-sample.md': 'samples/Maui/VolumeMonitor/README.md',
  'volume-verification.md': 'plugins/Plugin.VolumeMonitor/VERIFICATION.md',
  'volume-monitor.md': 'plugins/Plugin.VolumeMonitor/README.md',
  'audio-streamer.md': 'plugins/Plugin.AudioStreamer/README.md',
  'audio-verification.md': 'plugins/Plugin.AudioStreamer/VERIFICATION.md',
  'audio-sample.md': 'samples/Maui/AudioStreamer/README.md',
  'audio-avalonia-windows.md': 'samples/Avalonia/AudioStreamer/README.md',
  'audio-avalonia-linux.md': 'samples/Avalonia/AudioStreamer/Linux/README.md',
  'currency.md': 'plugins/Plugin.Currency/README.md',
  'currency-verification.md': 'plugins/Plugin.Currency/VERIFICATION.md',
  'sample.md': 'samples/Maui/ChannelExplorer/README.md',
  'currency-sample.md': 'samples/Maui/Currency/README.md',
  'currency-avalonia.md': 'samples/Avalonia/Currency/Currency.Android/README.md',
  'currency-avalonia-ios.md': 'samples/Avalonia/Currency/Currency.iOS/README.md',
  'currency-avalonia-windows.md': 'samples/Avalonia/Currency/Currency.Desktop/Windows.md',
  'currency-avalonia-macos.md': 'samples/Avalonia/Currency/Currency.Desktop/README.md',
  'currency-avalonia-linux.md': 'samples/Avalonia/Currency/Currency.Desktop/Linux.md',
  'currency-avalonia-browser.md': 'samples/Avalonia/Currency/Currency.Browser/README.md',
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
