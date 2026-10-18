#include "pdf.hpp"
#include <QApplication>
#include <QPdfDocument>
#include <QPdfPageNavigator>
#include <QPdfView>
#include <QWindow>
#include <memory>

class QtPdf final : public PdfModule {
  std::unique_ptr<QPdfView> view;
  std::unique_ptr<QPdfDocument> document;
  std::unique_ptr<QWindow> parentWindow;
  QSize previous;
  void open(const std::string &path) override {
    auto next = std::make_unique<QPdfDocument>();
    if (next->load(QString::fromUtf8(path.c_str())) !=
            QPdfDocument::Error::None ||
        next->pageCount() == 0)
      throw nc::Error(
          "pdf_open_failed",
          "Cannot open this PDF; use a readable, unencrypted local file");
    view->setDocument(next.get());
    document = std::move(next);
    count = document->pageCount();
  }
  void update() override {
    if (!document || !count)
      return;
    auto size = document->pagePointSize(page - 1);
    double fit =
        std::min(std::max(1, view->viewport()->width() - 20) / size.width(),
                 std::max(1, view->viewport()->height() - 20) / size.height());
    view->setZoomMode(QPdfView::ZoomMode::Custom);
    view->setZoomFactor(fit * zoom);
    view->pageNavigator()->jump(page - 1, QPointF(), fit * zoom);
  }

public:
  using PdfModule::PdfModule;
  uintptr_t create(uintptr_t parent, const char *id) override {
    // QApplication must outlive every native Qt widget and is shared by all
    // view instances.
    static int argc = 1;
    static char name[] = "nativechannels-pdf";
    static char *argv[] = {name, nullptr};
    static std::unique_ptr<QApplication> app;
    if (!QApplication::instance())
      app = std::make_unique<QApplication>(argc, argv);
    identity = id;
    parentWindow.reset(QWindow::fromWinId(parent));
    if (!parentWindow)
      throw nc::Error("pdf_host_failed", "Cannot adopt the X11 parent");
    view = std::make_unique<QPdfView>(nullptr);
    view->setPageMode(QPdfView::PageMode::SinglePage);
    auto handle = view->winId();
    view->windowHandle()->setParent(parentWindow.get());
    view->show();
    return handle;
  }
  void poll() override {
    QApplication::processEvents();
    if (view && previous != view->size()) {
      previous = view->size();
      update();
    }
  }
  ~QtPdf() override {
    if (view)
      view->setDocument(nullptr);
    view.reset();
    document.reset();
    parentWindow.reset();
  }
};
namespace nc {
Module *make_module(nc_reply reply) { return new QtPdf(reply); }
} // namespace nc
