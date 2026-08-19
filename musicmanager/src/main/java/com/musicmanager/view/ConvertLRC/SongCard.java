package com.musicmanager.view.ConvertLRC;

import java.awt.BorderLayout;
import java.awt.Cursor;
import java.awt.Dimension;
import java.awt.Font;
import java.awt.FontMetrics;
import java.awt.Graphics;
import java.awt.Graphics2D;
import java.awt.RenderingHints;
import java.awt.event.MouseAdapter;
import java.awt.event.MouseEvent;
import java.awt.geom.RoundRectangle2D;
import java.awt.image.BaseMultiResolutionImage;
import java.awt.image.BufferedImage;
import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.util.function.Consumer;

import javax.imageio.ImageIO;
import javax.swing.BorderFactory;
import javax.swing.Box;
import javax.swing.BoxLayout;
import javax.swing.ImageIcon;
import javax.swing.JLabel;
import javax.swing.JPanel;
import javax.swing.SwingConstants;

import com.musicmanager.entity.Music;
import com.musicmanager.view.Theme;

/**
 * a single flat, rounded card representing one song in the 2-column song
 * grid: cover art (or a placeholder), title, artist, and a color-coded
 * conversion status. Clicking it invokes onClick so the caller can offer
 * per-song conversion options.
 */
public class SongCard extends JPanel {

    private static final int THUMBNAIL_SIZE = 48;

    private final Music music;
    private final JLabel statusLabel;
    private boolean hovering = false;

    public SongCard(Music music, Consumer<Music> onClick) {
        super(new BorderLayout(Theme.COMPONENT_GAP, 0));
        this.music = music;
        setOpaque(false);
        setPreferredSize(new Dimension(0, 76));
        setCursor(Cursor.getPredefinedCursor(Cursor.HAND_CURSOR));
        setBorder(BorderFactory.createEmptyBorder(0, Theme.COMPONENT_GAP, 0, Theme.COMPONENT_GAP));

        JLabel thumbnail = new JLabel(buildThumbnailIcon(music.getCoverArt()));
        thumbnail.setHorizontalAlignment(SwingConstants.CENTER);
        add(thumbnail, BorderLayout.WEST);

        // BorderLayout.WEST centers the thumbnail vertically by default, but a plain
        // BoxLayout top-anchors its children with no glue, so without the glue below
        // the text block would sit visibly higher than the thumbnail it's next to
        JPanel textPanel = new JPanel();
        textPanel.setOpaque(false);
        textPanel.setLayout(new BoxLayout(textPanel, BoxLayout.Y_AXIS));
        textPanel.add(Box.createVerticalGlue());

        JLabel titleLabel = new JLabel(music.getTitle());
        titleLabel.setFont(Theme.bodyFont());
        titleLabel.setForeground(Theme.TEXT_PRIMARY);
        titleLabel.setAlignmentX(LEFT_ALIGNMENT);
        titleLabel.setToolTipText(music.getTitle());
        textPanel.add(titleLabel);

        String artistText = music.getArtists() != null && !music.getArtists().isEmpty()
                ? String.join(", ", music.getArtists())
                : null;
        if (artistText != null) {
            JLabel artistLabel = new JLabel(artistText);
            artistLabel.setFont(Theme.captionFont());
            artistLabel.setForeground(Theme.TEXT_SECONDARY);
            artistLabel.setAlignmentX(LEFT_ALIGNMENT);
            textPanel.add(artistLabel);
        }

        statusLabel = new JLabel(" ");
        statusLabel.setFont(Theme.statusFont());
        statusLabel.setAlignmentX(LEFT_ALIGNMENT);
        textPanel.add(statusLabel);

        textPanel.add(Box.createVerticalGlue());

        add(textPanel, BorderLayout.CENTER);

        addMouseListener(new MouseAdapter() {
            @Override
            public void mouseClicked(MouseEvent e) {
                // a modal dialog opens synchronously from here, which steals the mouse
                // before AWT gets a chance to fire mouseExited; clear the hover state
                // up front so the card doesn't render as hovered once it's closed
                hovering = false;
                repaint();
                onClick.accept(music);
            }

            @Override
            public void mouseEntered(MouseEvent e) {
                hovering = true;
                repaint();
            }

            @Override
            public void mouseExited(MouseEvent e) {
                hovering = false;
                repaint();
            }
        });
    }

    public Music getMusic() {
        return music;
    }

    /** shows a neutral "in progress" status while a conversion is running */
    public void showConverting() {
        statusLabel.setText("Converting…");
        statusLabel.setForeground(Theme.TEXT_SECONDARY);
    }

    /** shows the color-coded result of the most recent conversion attempt */
    public void showResult(boolean success) {
        statusLabel.setText(success ? "Converted" : "Failed");
        statusLabel.setForeground(success ? Theme.SUCCESS : Theme.ERROR);
    }

    /**
     * builds a thumbnail icon carrying both a 1x and a 2x resolution
     * variant (BaseMultiResolutionImage), so Swing picks the sharper one on
     * a HiDPI/scaled display instead of stretching a single 48x48 raster
     */
    private ImageIcon buildThumbnailIcon(byte[] coverArt) {
        BufferedImage artwork = decodeCoverArt(coverArt);
        BufferedImage base = renderThumbnail(artwork, THUMBNAIL_SIZE);
        BufferedImage highRes = renderThumbnail(artwork, THUMBNAIL_SIZE * 2);
        return new ImageIcon(new BaseMultiResolutionImage(base, highRes));
    }

    /** draws the (already decoded) artwork, high-quality scaled to size x size with rounded corners, or a placeholder note glyph if there is none */
    private BufferedImage renderThumbnail(BufferedImage artwork, int size) {
        BufferedImage canvas = new BufferedImage(size, size, BufferedImage.TYPE_INT_ARGB);
        Graphics2D g2 = canvas.createGraphics();
        g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g2.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BILINEAR);
        g2.setRenderingHint(RenderingHints.KEY_RENDERING, RenderingHints.VALUE_RENDER_QUALITY);

        float cornerArc = Theme.CORNER_ARC * size / (float) THUMBNAIL_SIZE;
        RoundRectangle2D.Float clip = new RoundRectangle2D.Float(0, 0, size, size, cornerArc, cornerArc);

        if (artwork != null) {
            BufferedImage scaled = highQualityScale(artwork, size, size);
            g2.setClip(clip);
            g2.drawImage(scaled, 0, 0, size, size, null);
        } else {
            g2.setColor(Theme.SURFACE);
            g2.fill(clip);

            g2.setColor(Theme.TEXT_SECONDARY);
            int fontSize = Math.round(20f * size / THUMBNAIL_SIZE);
            g2.setFont(new Font(Theme.FONT_FAMILY, Font.PLAIN, fontSize));
            FontMetrics metrics = g2.getFontMetrics();
            String note = "♪";
            int textX = (size - metrics.stringWidth(note)) / 2;
            int textY = (size - metrics.getHeight()) / 2 + metrics.getAscent();
            g2.drawString(note, textX, textY);
        }

        g2.dispose();
        return canvas;
    }

    /**
     * scales an image toward the target size in repeated halving steps
     * (each step at most a 2x change) using bilinear interpolation. A
     * single large-ratio scale (e.g. a 1000px embedded cover shrunk
     * straight to 96px) produces visible aliasing/blockiness; repeated
     * gentle halving avoids that, and also smooths the reverse case of a
     * genuinely tiny source image being scaled up.
     *
     * @param source
     * @param targetWidth
     * @param targetHeight
     * @return
     */
    private BufferedImage highQualityScale(BufferedImage source, int targetWidth, int targetHeight) {
        BufferedImage current = source;
        int width = source.getWidth();
        int height = source.getHeight();

        while (width > targetWidth * 2 || height > targetHeight * 2) {
            width = Math.max(targetWidth, width / 2);
            height = Math.max(targetHeight, height / 2);
            current = scaleStep(current, width, height);
        }

        return scaleStep(current, targetWidth, targetHeight);
    }

    private BufferedImage scaleStep(BufferedImage source, int width, int height) {
        BufferedImage scaled = new BufferedImage(width, height, BufferedImage.TYPE_INT_ARGB);
        Graphics2D g2 = scaled.createGraphics();
        g2.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BILINEAR);
        g2.setRenderingHint(RenderingHints.KEY_RENDERING, RenderingHints.VALUE_RENDER_QUALITY);
        g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g2.drawImage(source, 0, 0, width, height, null);
        g2.dispose();
        return scaled;
    }

    private BufferedImage decodeCoverArt(byte[] coverArt) {
        if (coverArt == null || coverArt.length == 0) {
            return null;
        }
        try {
            return ImageIO.read(new ByteArrayInputStream(coverArt));
        } catch (IOException e) {
            return null;
        }
    }

    @Override
    protected void paintComponent(Graphics g) {
        Graphics2D g2 = (Graphics2D) g.create();
        g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);

        RoundRectangle2D.Float shape = new RoundRectangle2D.Float(0, 0, getWidth() - 1, getHeight() - 1,
                Theme.CORNER_ARC, Theme.CORNER_ARC);
        g2.setColor(Theme.CARD_BACKGROUND);
        g2.fill(shape);
        g2.setColor(hovering ? Theme.PRIMARY : Theme.BORDER);
        g2.draw(shape);

        g2.dispose();
        super.paintComponent(g);
    }
}
