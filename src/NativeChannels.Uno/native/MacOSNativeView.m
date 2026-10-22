#import <AppKit/AppKit.h>

// TODO: Remove with the public hosting API: https://github.com/unoplatform/uno/issues/25639
@interface NCUnoContainer : NSView
@property(nonatomic, weak) NSView *originalSuperView;
@end
@implementation NCUnoContainer
- (BOOL)isFlipped { return YES; }
// Uno invokes dispose on Unloaded. Managed ownership controls the final release.
- (void)dispose { [self removeFromSuperview]; }
@end

void *nc_uno_create(void *view) {
    NSView *child = (__bridge NSView *)view;
    if (!NSThread.isMainThread || !child || child.superview) return NULL;
    NCUnoContainer *container = [[NCUnoContainer alloc] initWithFrame:child.frame];
    child.frame = container.bounds;
    child.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable;
    [container addSubview:child];
    return (__bridge_retained void *)container;
}

int nc_uno_parent(void *pointer, void *window) {
    id owner = (__bridge id)window;
    if (!NSThread.isMainThread || !owner || ![owner respondsToSelector:@selector(renderingView)]) return 0;
    NCUnoContainer *container = (__bridge NCUnoContainer *)pointer;
    container.originalSuperView = [owner valueForKey:@"renderingView"];
    return container.originalSuperView != nil;
}

void nc_uno_release(void *pointer) {
    NCUnoContainer *container = CFBridgingRelease(pointer);
    for (NSView *child in container.subviews.copy) [child removeFromSuperview];
    [container removeFromSuperview];
    container.originalSuperView = nil;
}
