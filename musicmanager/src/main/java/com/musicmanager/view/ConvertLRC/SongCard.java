package com.musicmanager.view.ConvertLRC;

import java.awt.BorderLayout;
import java.awt.Cursor;
import java.awt.Dimension;
import java.awt.Graphics;
import java.awt.Graphics2D;
import java.awt.RenderingHints;
import java.awt.event.MouseAdapter;
import java.awt.event.MouseEvent;
import java.awt.geom.RoundRectangle2D;
import java.util.function.Consumer;

import javax.swing.BorderFactory;
import javax.swing.BoxLayout;
import javax.swing.JLabel;
import javax.swing.JPanel;

import com.musicmanager.entity.Music;
import com.musicmanager.view.Theme;

/**
 * a single flat, rounded card representing one song in the 2-column song
 * grid. Clicking it invokes onClick so the caller can offer per-song
 * conversion options; showConverting/showResult reflect that song's
 * conversion status directly on the card.
 */
public class SongCard extends JPanel {

    private final Music music;
    private final JLabel statusLabel;
    private boolean hovering = false;

    public SongCard(Music music, Consumer<Music> onClick) {
        super(new BorderLayout());
        this.music = music;
        setOpaque(false);
        setPreferredSize(new Dimension(0, 64));
        setCursor(Cursor.getPredefinedCursor(Cursor.HAND_CURSOR));

        JPanel textPanel = new JPanel();
        textPanel.setOpaque(false);
        textPanel.setLayout(new BoxLayout(textPanel, BoxLayout.Y_AXIS));
        textPanel.setBorder(BorderFactory.createEmptyBorder(0, Theme.COMPONENT_GAP, 0, Theme.COMPONENT_GAP));

        JLabel titleLabel = new JLabel(music.getTitle());
        titleLabel.setFont(Theme.bodyFont());
        titleLabel.setForeground(Theme.TEXT_PRIMARY);
        titleLabel.setAlignmentX(LEFT_ALIGNMENT);
        titleLabel.setToolTipText(music.getTitle());
        textPanel.add(titleLabel);

        statusLabel = new JLabel(" ");
        statusLabel.setFont(Theme.statusFont());
        statusLabel.setAlignmentX(LEFT_ALIGNMENT);
        textPanel.add(statusLabel);

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
