MapGen
======

This is something I put together for a University project based on
[ActionStreetMap](https://github.com/ActionStreetMap), a very cool Unity
framework for building maps from [OpenStreetMap](http://www.openstreetmap.org)
data.

The ActionStreetMap demo builds up the world as you move around it, but for
this project I wanted to pre-generate the world so that at runtime nothing
gets generated. Therefore, I created this project based on the ASM demo code,
to generate the entirety of a map area (specified via properties in the Unity
editor) and then export all the data as prefabs. This can be packaged up and
imported into another project.

Note that the implementation is fairly hacky as the project has a short time
scale, but it works and might be useful to someone else.

All credit should go to the ActionStreetMap author(s), I've just reworked
their demo to do this!
