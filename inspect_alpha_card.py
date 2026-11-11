import bpy
img=bpy.data.images.load(r'C:\Protergia\pine_sprig_card.png')
a=list(img.pixels)[3::4]
for t in [0,0.0001,0.001,0.01,0.1,0.25,0.5,0.75,0.99,0.999]: print(t,sum(x>t for x in a)/len(a))
print('SAMPLES',a[0],a[len(a)//2],a[-1])
