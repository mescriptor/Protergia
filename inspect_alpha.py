import bpy, os
from collections import Counter
p=r'C:\Users\Yannis\OneDrive\Υπολογιστής\Protergia\Models\peuko'
for name in ['texture_diffuse.png','shaded.png','texture_pbr.png','texture_roughness.png']:
 img=bpy.data.images.load(os.path.join(p,name),check_existing=False)
 px=list(img.pixels)
 a=px[3::4]
 print(name, 'size',tuple(img.size), 'channels',img.channels,'alpha_range',min(a),max(a),'alpha_lt_0.99',sum(1 for x in a if x<0.99)/len(a),'alpha_lt_0.5',sum(1 for x in a if x<0.5)/len(a))
