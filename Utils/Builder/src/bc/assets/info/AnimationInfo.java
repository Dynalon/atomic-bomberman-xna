package bc.assets.info;

import java.awt.image.BufferedImage;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.IOException;
import java.util.List;

import javax.imageio.ImageIO;

import org.apache.tools.ant.BuildException;
import org.dom4j.Document;
import org.dom4j.Element;
import org.dom4j.io.SAXReader;

import bc.assets.AssetContext;
import bc.assets.AssetInfo;
import bc.assets.AssetRegistry;
import bc.assets.BinaryWriter;
import bc.assets.ContentImporter;
import bc.assets.ContentInfo;
import bc.assets.ContentWriter;
import bc.assets.types.Animation;
import bc.assets.types.Animation.AnimationFrame;
import bc.assets.types.Texture;

public class AnimationInfo extends AssetInfo
{
	static
	{
		ContentInfo<AnimationInfo, Animation> info = new ContentInfo<AnimationInfo, Animation>();
		info.importer = new AnimationImporter();
		info.writer = new AnimationWriter();
		
		AssetRegistry.register(AnimationInfo.class, info);
	}
	
	private File textureFile;
	
	public AnimationInfo()
	{
		super("Animation");
	}
	
	public void setTexture(File texture)
	{
		this.textureFile = texture;
	}
	
	public File getTextureFile()
	{
		return textureFile;
	}
}

class AnimationImporter extends ContentImporter<AnimationInfo, Animation>
{
	@Override
	public Animation importAsset(AnimationInfo info, AssetContext context) throws IOException
	{
		Element root = readDocument(info.getFile()).getRootElement();
		
		Texture texture = readTexture(root, info.getParentFile());
		
		String name = attributeString(root, "name");
		Animation animation = new Animation(name, texture);
		
		List<Element> frameElements = root.elements("frame");
		for (Element frameElement : frameElements)
		{
			animation.addFrame(readFrame(frameElement));
		}
		
		return animation;
	}

	private Texture readTexture(Element element, File baseDir) throws IOException
	{
		String textureName = attributeString(element, "file");
		File textureFile = new File(baseDir, textureName);
		if (!textureFile.exists())
			throw new BuildException("File not exists: " + textureFile);
		
		BufferedImage textureImage = ImageIO.read(new File(baseDir, textureName));
		Texture texture = new Texture(textureImage);
		return texture;
	}

	private AnimationFrame readFrame(Element element)
	{
		AnimationFrame frame = new AnimationFrame();
		
		frame.x = attributeInt(element, "x");
		frame.y = attributeInt(element, "y");
		frame.ox = attributeInt(element, "ox");
		frame.oy = attributeInt(element, "oy");
		frame.w = attributeInt(element, "w");
		frame.h = attributeInt(element, "h");
		frame.duration = attributeInt(element, "duration", 50);
		
		return frame;
	}

	private Document readDocument(File file)
	{
		try
		{
			return new SAXReader().read(file);
		}
		catch (Exception e)
		{
			throw new BuildException(e);
		}
	}
	
	private String attributeString(Element element, String name)
	{
		String value = element.attributeValue(name);
		return value;
	}
	
	private int attributeInt(Element element, String name)
	{
		return attributeInt(element, name, 0);
	}
	
	private int attributeInt(Element element, String name, int defaultValue)
	{
		String value = attributeString(element, name);
		if (value != null)
		{
			try
			{
				return Integer.parseInt(value);
			}
			catch (NumberFormatException e)
			{
				throw new BuildException(e);
			}
		}
		
		return defaultValue;
	}
}

class AnimationWriter extends ContentWriter<Animation>
{
	@Override
	protected void write(BinaryWriter output, Animation animation, AssetContext context) throws IOException
	{
		output.write(animation.getName());
		List<AnimationFrame> frames = animation.getFrames();
		output.write(frames.size());
		
		for (AnimationFrame frame : frames)
		{
			write(output, frame);
		}
		
		Texture texture = animation.getTexture();
		write(output, texture);
	}

	private void write(BinaryWriter output, AnimationFrame frame) throws IOException
	{
		output.write(frame.x);
		output.write(frame.y);
		output.write(frame.ox);
		output.write(frame.oy);
		output.write(frame.w);
		output.write(frame.h);
		output.write(frame.duration);
	}

	private void write(BinaryWriter output, Texture texture) throws IOException
	{
		ByteArrayOutputStream bos = new ByteArrayOutputStream();
		ImageIO.write(texture.getImage(), "png", bos);
		
		byte[] data = bos.toByteArray();
		output.write(data.length);
		output.write(data);
	}
}

