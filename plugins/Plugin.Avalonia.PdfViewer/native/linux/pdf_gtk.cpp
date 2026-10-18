#include "pdf.hpp"
#include <X11/Xlib.h>
#include <gdk/gdkx.h>
#include <gtk/gtk.h>
#include <poppler.h>

class GtkPdf final : public PdfModule {
  GtkWidget *window = nullptr, *scroll = nullptr, *canvas = nullptr;
  PopplerDocument *document = nullptr;
  int width = 0, height = 0;
  double scale = 1;
  void open(const std::string &path) override {
    GError *error = nullptr;
    char *uri = g_filename_to_uri(path.c_str(), nullptr, &error);
    if (!uri) {
      if (error)
        g_error_free(error);
      throw nc::Error("pdf_open_failed", "Invalid file path");
    }
    auto next = poppler_document_new_from_file(uri, nullptr, &error);
    g_free(uri);
    if (!next || poppler_document_get_n_pages(next) < 1) {
      if (next)
        g_object_unref(next);
      std::string message = error ? error->message : "The PDF has no pages";
      if (error)
        g_error_free(error);
      throw nc::Error("pdf_open_failed", message);
    }
    if (document)
      g_object_unref(document);
    document = next;
    count = poppler_document_get_n_pages(document);
  }
  void update() override {
    if (!document || !page)
      return;
    auto current = poppler_document_get_page(document, page - 1);
    double w, h;
    poppler_page_get_size(current, &w, &h);
    g_object_unref(current);
    scale =
        std::min(std::max(1, width - 20) / w, std::max(1, height - 20) / h) *
        zoom;
    gtk_widget_set_size_request(canvas, std::max(1, int(w * scale)),
                                std::max(1, int(h * scale)));
    gtk_widget_queue_draw(canvas);
  }
  static gboolean draw(GtkWidget *, cairo_t *cr, gpointer data) {
    auto self = static_cast<GtkPdf *>(data);
    cairo_set_source_rgb(cr, 1, 1, 1);
    cairo_paint(cr);
    if (self->document && self->page) {
      auto current = poppler_document_get_page(self->document, self->page - 1);
      cairo_scale(cr, self->scale, self->scale);
      poppler_page_render(current, cr);
      g_object_unref(current);
    }
    return FALSE;
  }

public:
  using PdfModule::PdfModule;
  uintptr_t create(uintptr_t parent, const char *id) override {
    if (!gtk_init_check(nullptr, nullptr) ||
        !GDK_IS_X11_DISPLAY(gdk_display_get_default()))
      throw nc::Error("pdf_host_failed", "GTK PDF requires an X11 display");
    identity = id;
    window = gtk_window_new(GTK_WINDOW_TOPLEVEL);
    gtk_window_set_decorated(GTK_WINDOW(window), FALSE);
    scroll = gtk_scrolled_window_new(nullptr, nullptr);
    canvas = gtk_drawing_area_new();
    gtk_container_add(GTK_CONTAINER(scroll), canvas);
    gtk_container_add(GTK_CONTAINER(window), scroll);
    g_signal_connect(canvas, "draw", G_CALLBACK(draw), this);
    gtk_widget_realize(window);
    auto native = gtk_widget_get_window(window);
    auto display = GDK_WINDOW_XDISPLAY(native);
    auto xid = GDK_WINDOW_XID(native);
    XReparentWindow(display, xid, parent, 0, 0);
    gtk_widget_show_all(window);
    XFlush(display);
    return xid;
  }
  void poll() override {
    for (int i = 0; i < 16 && gtk_events_pending(); ++i)
      gtk_main_iteration_do(FALSE);
    int w = gtk_widget_get_allocated_width(scroll),
        h = gtk_widget_get_allocated_height(scroll);
    if (width != w || height != h) {
      width = w;
      height = h;
      update();
    }
  }
  ~GtkPdf() override {
    if (window)
      gtk_widget_destroy(window);
    if (document)
      g_object_unref(document);
  }
};
namespace nc {
Module *make_module(nc_reply reply) { return new GtkPdf(reply); }
} // namespace nc
