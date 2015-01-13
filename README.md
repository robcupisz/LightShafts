Light shafts
============
A light shafts (aka light scattering, aka volumetric shadows) effect for Unity.

**[video](http://files.unity3d.com/rcupisz/LightShafts/v.mp4)**

![spot light](http://files.unity3d.com/rcupisz/LightShafts/0.png)
![directional and spot light](http://files.unity3d.com/rcupisz/LightShafts/1.png)

Performance: in 1080p on GTX580 about 1.0-1.5ms for a full screen light, down to 0.2ms if the light is smaller or partially occluded.

Download
--------
Check out this repo into a subfolder of your Unity project (visible meta files), e.g. `Assets/LightShafts/`

Light shafts require Unity Pro and should work on Windows (DX9 and DX11) and OSX.

*Warning*, for now the status is "works for me", so watch out for bugs. Pull requests with fixes welcome.

Version 2. Verified in Unity 4.5.5f1.

Usage
-----
Add the LightShafts.cs script to a directional light or spot light and tweak the settings.

In general volumetric lighting is a very expensive effect. This implementation tries to make it affordable by avoiding slow raymarching for every screen pixel. A smaller number of raymarching samples in important places is chosen instead (red pixels in images below) and the final lighting is interpolated from those.

![sampling](http://files.unity3d.com/rcupisz/LightShafts/2.png)

It's important to tweak the effect's quality settings to **get as few red (expensive) samples as possible**. Other settings are important for performance too.

- Start out by tweaking the *size* (directional light) or *spot angle* and *volume start and end* (spot light) to get the yellow box/frustum in the scene view tightly around your target area.
- Set the *culling mask* to only include objects which need to cast volumetric shadow (doesn't matter if the *shadowmap mode* is static, as then the shadowmap is only rendered at startup).
- Enable *show samples* (DX11 only for now).
- Tweak *shadowmap resolution* to be as low as possible, but still be able to make out the detail in silhouettes of shadow casters.
- *Samples across rays* - that many samples - and rays - around the light, when looking at it.
- *Samples along rays* - that many *potential* samples along each ray, but they only become actual samples if they encounter a difference in depth or are forced by the *force samples every* setting.
- *Depth threshold* - from camera's perspective, light shafts change intensity wherever there's a bigger depth difference. Make sure this setting creates silhouettes of red pixels around objects where it matters.
- *Force samples every* - even if there's no abrupt change in depth, light shafts' intensity still changes along it's length somewhat and that gradient needs to be sampled. Set to a higher value if you can (the goal is still to have as few red samples as possible).

### Colored light shafts

To get the effect of light tinted by stained glass, enable the *colored* checkbox and set the *color filter* layer mask to whatever layer contains your colored objects. Those objects will be rendered to a buffer using a forward rendering camera, so sometimes it might be better to create duplicates with a shader not using lighting, just outputting saturated color. The rays will be tinted along their entire length.

Cookies on spot lights are multiplied in as well, and also affect performance.

### LightShafts.cs vs SunShafts.js

The SunShafts.js effect in standard assets performs a (depth-aware) radial blur of the skybox, fully in screen space. So the effect is more *volatile*, visible only when looking against the light direction, etc., but also cheaper. Use LightShafts.cs when you need a more *grounded* effect, in world space, visible from the side - and can afford it.

What's next?
------------
- I'll probably add the effect to Unity's image effects standard package, when it's done.
- 1D min/max mipmap optimization: not sure if I'll implement it. It's usefullness is limited to dx11 and non-colored lights without cookies, mostly. Pull requests welcome, though :)
- Some dithering would be nice to avoid banding in dark scenes.
- Re-using the internal shadowmaps instead of rendering new ones - hmm...
- Cookies: for directional lights too (need an offset setting) and premultiply with color filter, if both are enabled.
- Make sample visualisation work on dx9 and opengl as well.

Links
-----
- [Original paper](http://www.sfb716.uni-stuttgart.de/uploads/tx_vispublications/espmss10.pdf) on epipolar sampling by Thomas Engelhardt and Carsten Dachsbacher.
- An [article](http://software.intel.com/en-us/articles/ivb-atmospheric-light-scattering) and [code sample](http://software.intel.com/en-us/blogs/2013/03/18/gtd-light-scattering-sample-updated) on Intel's website.

License
-------
Public domain.
