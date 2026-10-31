using System.Windows.Media.Media3D;

namespace Mindie.Controls;

internal static class GraphGeometry3D
{
    internal static readonly MeshGeometry3D Sphere=MakeSphere();
    internal static readonly MeshGeometry3D Cylinder=MakeCylinder();
    private static MeshGeometry3D MakeSphere()
    {
        var mesh=new MeshGeometry3D(); const int rows=8,columns=12;
        for(var row=0;row<=rows;row++) for(var column=0;column<=columns;column++) {
            var theta=Math.PI*row/rows; var phi=Math.PI*2*column/columns;
            var point=new Point3D(Math.Sin(theta)*Math.Cos(phi),Math.Cos(theta),Math.Sin(theta)*Math.Sin(phi));
            mesh.Positions.Add(point); mesh.Normals.Add(new(point.X,point.Y,point.Z));
        }
        for(var row=0;row<rows;row++) for(var col=0;col<columns;col++) {
            var a=row*(columns+1)+col; var b=a+columns+1;
            Triangle(mesh,a,a+1,b); Triangle(mesh,a+1,b+1,b);
        }
        mesh.Freeze(); return mesh;
    }
    private static MeshGeometry3D MakeCylinder()
    {
        var mesh=new MeshGeometry3D(); const int sides=8;
        for(var i=0;i<=sides;i++) {
            var angle=i*Math.PI*2/sides; var x=Math.Cos(angle); var z=Math.Sin(angle);
            mesh.Positions.Add(new(x,0,z)); mesh.Positions.Add(new(x,1,z)); mesh.Normals.Add(new(x,0,z)); mesh.Normals.Add(new(x,0,z));
        }
        for(var i=0;i<sides;i++) { var a=i*2; Triangle(mesh,a,a+1,a+2); Triangle(mesh,a+1,a+3,a+2); }
        mesh.Freeze(); return mesh;
    }
    internal static Transform3D LinkTransform(Point3D from,Point3D to,double radius)
    {
        var direction=to-from; var length=direction.Length; if(length<.00001) return new ScaleTransform3D(0,0,0);
        direction.Normalize(); var axis=Vector3D.CrossProduct(new(0,1,0),direction);
        var angle=Math.Acos(Math.Clamp(direction.Y,-1,1))*180/Math.PI;
        if(axis.LengthSquared<.000001) axis=new(1,0,0); else axis.Normalize();
        return new Transform3DGroup { Children=new() {
            new ScaleTransform3D(radius,length,radius),new RotateTransform3D(new QuaternionRotation3D(new Quaternion(axis,angle))),new TranslateTransform3D(from.X,from.Y,from.Z)
        } };
    }
    private static void Triangle(MeshGeometry3D mesh,int a,int b,int c) { mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(c); }
}
