Zombie Rush branding generator (.NET 10 + SkiaSharp 3.119.0 + Svg.Skia 3.0.3)

gen/  - thumbnail (Thumb.cs) + icon (IconArt.cs, painted zombie in ZombieFace.cs)
        cd gen && dotnet run -c Release -- ../svg <outDir>
        FACE_TEST=1 -> renders only the zombie face on magenta (debug)
        Reads logo/fonts/sky from the Unity project (path set in Program.cs P.Proj).
svg/  - game-icons.net silhouettes (CC BY 3.0) - only skoll_ak47 is used in the final art.
r3d/  - binary FBX reader + software renderer (used for an earlier icon try; not used now).
