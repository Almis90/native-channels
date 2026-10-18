"""Regenerate the original, MIT-licensed demo document with reportlab."""
from pathlib import Path
from reportlab.pdfgen import canvas
from reportlab.lib.colors import HexColor, white

output = Path(__file__).parent / 'Raw/nativechannels-guide.pdf'
c = canvas.Canvas(str(output), pagesize=(540, 700), invariant=1)
c.setTitle('Native views, shared experience')
c.setAuthor('NativeChannels contributors')
pages = [
    ('01', 'Native views,', 'shared experience.', 'A small field guide to NativeChannels',
     [('SWIFT', 'PDFKit renders this document on Apple platforms.'),
      ('KOTLIN', 'PdfRenderer paints each page on Android.'),
      ('C#', 'MAUI controls navigation through a private channel.')]),
    ('02', 'One view.', 'Its own channel.', 'Commands go out. Native state comes back.',
     [('OPEN', 'Load a local file without sending PDF bytes as JSON.'),
      ('NAVIGATE', 'Jump to a page or set a zoom factor from C#.'),
      ('OBSERVE', 'Native gestures report page and zoom changes.')]),
    ('03', 'Built to', 'disconnect safely.', 'Documents have a lifetime, just like views.',
     [('ISOLATE', 'Two viewers keep independent documents and state.'),
      ('DETACH', 'Release observers, bitmaps and document handles.'),
      ('RECONNECT', 'Create a fresh view and reopen the document.')]),
]
for number, title, subtitle, tagline, items in pages:
    c.setFillColor(HexColor('#f5f7fb')); c.rect(0,0,540,700,fill=1,stroke=0)
    c.setFillColor(HexColor('#12263a')); c.rect(0,386,540,314,fill=1,stroke=0)
    c.setFillColor(HexColor('#52d6be')); c.setFont('Helvetica-Bold',11); c.drawString(40,652,'NATIVECHANNELS / PDF VIEWER')
    c.setFillColor(white); c.setFont('Helvetica-Bold',38)
    c.drawString(40,565,title); c.drawString(40,518,subtitle)
    c.setFont('Helvetica',13); c.drawString(40,464,tagline)
    c.setFillColor(HexColor('#52d6be')); c.rect(40,416,86,5,fill=1,stroke=0)
    y=332
    for label, body in items:
        c.setFillColor(HexColor('#087e75')); c.setFont('Helvetica-Bold',12); c.drawString(40,y,label)
        c.setFillColor(HexColor('#334155')); c.setFont('Helvetica',12); c.drawString(40,y-25,body)
        y-=87
    c.setStrokeColor(HexColor('#ccd5df')); c.line(40,61,500,61)
    c.setFont('Helvetica',10); c.setFillColor(HexColor('#64748b'))
    c.drawString(40,39,'Original demo document / MIT license'); c.drawRightString(500,39,f'{number} / 03')
    c.showPage()
c.save()
print(output)
