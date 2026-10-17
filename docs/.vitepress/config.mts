import { defineConfig } from 'vitepress'
import { repository, repositoryLinks, sourcePath } from './repository-links.mjs'

export default defineConfig({
  title: 'NativeChannels',
  description: 'Native platform channels for .NET MAUI. Connect C# with Kotlin, Swift, and Windows APIs through generic bridges.',
  base: '/native-channels/',
  cleanUrls: true,
  lastUpdated: true,
  sitemap: { hostname: 'https://almis90.github.io/native-channels/' },
  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/native-channels/logo.svg' }],
    ['meta', { name: 'theme-color', content: '#6554c0' }],
  ],
  markdown: { config: repositoryLinks },
  transformPageData(page) {
    page.frontmatter.sourcePath = sourcePath(page.relativePath)
  },
  themeConfig: {
    logo: '/logo.svg',
    search: { provider: 'local' },
    nav: [
      { text: 'Guide', link: '/getting-started' },
      { text: 'Examples', link: '/examples' },
      { text: 'Build a plugin', link: '/plugin-authoring' },
      { text: 'API', link: '/api-reference' },
      { text: 'Project', items: [
        { text: 'Changelog', link: '/changelog' },
        { text: 'Roadmap', link: '/roadmap' },
        { text: 'Verified platforms', link: '/verification' },
      ] },
    ],
    sidebar: [
      { text: 'Start here', items: [
        { text: 'Getting started', link: '/getting-started' },
        { text: 'Choose a channel', link: '/choosing-a-channel' },
        { text: 'Examples', link: '/examples' },
        { text: 'Documentation guide', link: '/overview' },
      ] },
      { text: 'Channels and views', items: [
        { text: 'Method channels', link: '/method-channels' },
        { text: 'Basic messages', link: '/basic-message-channels' },
        { text: 'Event streams', link: '/event-channels' },
        { text: 'Native views', link: '/native-views' },
      ] },
      { text: 'Configuration and behavior', collapsed: false, items: [
        { text: 'Configuration', link: '/configuration' },
        { text: 'Codecs and typed values', link: '/message-codecs' },
        { text: 'Error handling', link: '/error-handling' },
        { text: 'Threading', link: '/threading' },
        { text: 'Lifecycle', link: '/lifecycle' },
        { text: 'C# API reference', link: '/api-reference' },
      ] },
      { text: 'Build a plugin', collapsed: false, items: [
        { text: 'Plugin authoring', link: '/plugin-authoring' },
        { text: 'Android', link: '/android-integration' },
        { text: 'Apple / iOS', link: '/ios-integration' },
        { text: 'Mac Catalyst', link: '/maccatalyst-integration' },
        { text: 'Windows', link: '/windows-integration' },
      ] },
      { text: 'Reference examples', collapsed: true, items: [
        { text: 'Currency', collapsed: true, items: [
          { text: 'Implementation', link: '/currency' },
          { text: 'Sample app', link: '/currency-sample' },
          { text: 'Avalonia Android proof', link: '/currency-avalonia' },
          { text: 'Verification', link: '/currency-verification' },
        ] },
        { text: 'AudioStreamer', collapsed: true, items: [
          { text: 'Implementation', link: '/audio-streamer' },
          { text: 'Sample app', link: '/audio-sample' },
          { text: 'Verification', link: '/audio-verification' },
        ] },
        { text: 'VolumeMonitor', collapsed: true, items: [
          { text: 'Implementation', link: '/volume-monitor' },
          { text: 'Sample app', link: '/volume-sample' },
          { text: 'Verification', link: '/volume-verification' },
        ] },
        { text: 'PdfViewer', collapsed: true, items: [
          { text: 'Implementation', link: '/pdf-viewer' },
          { text: 'Sample app', link: '/pdf-sample' },
          { text: 'Verification', link: '/pdf-verification' },
        ] },
        { text: 'Framework verification app', link: '/sample' },
      ] },
      { text: 'Understand the bridge', collapsed: true, items: [
        { text: 'Architecture', link: '/architecture' },
        { text: 'Binary messenger', link: '/binary-messenger' },
        { text: 'JSON protocol', link: '/message-protocol' },
        { text: 'Flutter comparison', link: '/flutter-comparison' },
      ] },
      { text: 'Development', collapsed: true, items: [
        { text: 'Building from source', link: '/building' },
        { text: 'Testing', link: '/testing' },
        { text: 'Performance and stress', link: '/performance' },
        { text: 'Release checklist', link: '/release-checklist' },
        { text: 'Publishing to NuGet', link: '/publishing' },
        { text: 'Roadmap', link: '/roadmap' },
        { text: 'Verified platforms', link: '/verification' },
        { text: 'Contributing', link: '/contributing' },
        { text: 'Documentation site', link: '/documentation-site' },
        { text: 'Changelog', link: '/changelog' },
      ] },
      { text: 'Help', items: [
        { text: 'Troubleshooting', link: '/troubleshooting' },
        { text: 'FAQ', link: '/faq' },
      ] },
    ],
    socialLinks: [{ icon: 'github', link: repository }],
    editLink: {
      pattern: (page) => 'https://github.com/Almis90/native-channels/edit/main/' + page.frontmatter.sourcePath,
      text: 'Edit this page on GitHub',
    },
    footer: { message: 'Released under the MIT License.', copyright: 'Copyright © 2026 NativeChannels contributors' },
    outline: [2, 3],
  },
})
