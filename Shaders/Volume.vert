#version 450

layout(location = 0) in vec3 in_Position;

layout(location = 0) out vec3 out_TexCoord;

void main() 
{
    // The vertex shader simply generates a fullscreen triangle.
    // The fragment shader will use the vertex ID to calculate screen coordinates.
    out_TexCoord = in_Position;
    gl_Position = vec4(in_Position * 2.0 - 1.0, 1.0);
    // Invert Y for Veldrid's coordinate system
    gl_Position.y = -gl_Position.y;
}