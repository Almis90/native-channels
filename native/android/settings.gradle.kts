pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "NativeChannels"

include(":library", ":sample")

include(":currency")

project(":currency").projectDir = file("../../plugins/Plugin.Currency/native/android")

include(":audio")

project(":audio").projectDir = file("../../plugins/Plugin.AudioStreamer/native/android")

include(":volume")

project(":volume").projectDir = file("../../plugins/Plugin.VolumeMonitor/native/android")

include(":pdf")

project(":pdf").projectDir = file("../../plugins/Plugin.Maui.PdfViewer/native/android")
