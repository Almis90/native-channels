package dev.nativechannels.pdf

import android.content.Context
import android.graphics.*
import android.view.GestureDetector
import android.view.MotionEvent
import android.view.ScaleGestureDetector
import android.view.View

/**
 * Small native page surface: pinch/double-tap to zoom, drag to pan, swipe at fit to change page.
 */
internal class PdfCanvas(context: Context) : View(context) {
    var changePage: ((Int) -> Unit)? = null
    var changeZoom: ((Double) -> Unit)? = null
    private var bitmap: Bitmap? = null
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
    private var zoom = 1.0
    private var panX = 0f
    private var panY = 0f
    private val scale =
        ScaleGestureDetector(
            context,
            object : ScaleGestureDetector.SimpleOnScaleGestureListener() {
                override fun onScale(detector: ScaleGestureDetector): Boolean {
                    setZoom((zoom * detector.scaleFactor).coerceIn(1.0, 4.0), resetPan = false)
                    return true
                }

                override fun onScaleEnd(detector: ScaleGestureDetector) {
                    changeZoom?.invoke(zoom)
                }
            },
        )
    private val gestures =
        GestureDetector(
            context,
            object : GestureDetector.SimpleOnGestureListener() {
                override fun onDown(event: MotionEvent) = true

                override fun onSingleTapUp(event: MotionEvent): Boolean {
                    performClick()
                    return true
                }

                override fun onDoubleTap(event: MotionEvent): Boolean {
                    setZoom(if (zoom > 1.0) 1.0 else 2.0)
                    changeZoom?.invoke(zoom)
                    return true
                }

                override fun onScroll(
                    first: MotionEvent?,
                    last: MotionEvent,
                    dx: Float,
                    dy: Float,
                ): Boolean {
                    if (zoom > 1.0 && !scale.isInProgress) {
                        panX -= dx
                        panY -= dy
                        invalidate()
                    }
                    return true
                }

                override fun onFling(
                    first: MotionEvent?,
                    last: MotionEvent,
                    vx: Float,
                    vy: Float,
                ): Boolean {
                    if (
                        zoom == 1.0 &&
                            !scale.isInProgress &&
                            first != null &&
                            kotlin.math.abs(vx) > kotlin.math.abs(vy) &&
                            kotlin.math.abs(last.x - first.x) >
                                60 * resources.displayMetrics.density
                    ) {
                        changePage?.invoke(if (vx < 0) 1 else -1)
                    }
                    return true
                }
            },
        )

    init {
        setBackgroundColor(Color.rgb(226, 232, 240))
        contentDescription = "PDF document. Use the page and zoom buttons, or pinch and swipe."
        isFocusable = true
    }

    fun show(value: Bitmap, factor: Double) {
        bitmap = value
        setZoom(factor)
    }

    fun setZoom(value: Double, resetPan: Boolean = true) {
        zoom = value
        if (resetPan) {
            panX = 0f
            panY = 0f
        }
        invalidate()
    }

    fun clear() {
        bitmap = null
        changePage = null
        changeZoom = null
        invalidate()
    }

    override fun performClick(): Boolean {
        super.performClick()
        return true
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        parent?.requestDisallowInterceptTouchEvent(true)
        scale.onTouchEvent(event)
        gestures.onTouchEvent(event)
        if (
            event.actionMasked == MotionEvent.ACTION_UP ||
                event.actionMasked == MotionEvent.ACTION_CANCEL
        )
            parent?.requestDisallowInterceptTouchEvent(false)
        return true
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val image = bitmap ?: return
        val fit = minOf(width.toFloat() / image.width, height.toFloat() / image.height)
        val w = (image.width * fit * zoom).toFloat()
        val h = (image.height * fit * zoom).toFloat()
        panX = panX.coerceIn(-maxOf(0f, (w - width) / 2), maxOf(0f, (w - width) / 2))
        panY = panY.coerceIn(-maxOf(0f, (h - height) / 2), maxOf(0f, (h - height) / 2))
        val x = (width - w) / 2 + panX
        val y = (height - h) / 2 + panY
        canvas.drawBitmap(image, null, RectF(x, y, x + w, y + h), paint)
    }
}
