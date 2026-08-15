package com.musicmanager.view.ConvertLRC;

import java.awt.BorderLayout;
import java.awt.Dimension;
import java.awt.Graphics;
import java.awt.Graphics2D;
import java.awt.RenderingHints;
import java.awt.geom.RoundRectangle2D;

import javax.swing.BorderFactory;
import javax.swing.JLabel;
import javax.swing.JPanel;

import com.musicmanager.view.Theme;

/**
 * a single flat, rounded card representing one song in the 2-column song
 * grid. Read-only, non-interactive — this is a manifest, not a picker.
 */
public class SongCard extends JPanel {

    public SongCard(String title) {
        super(new BorderLayout());
        setOpaque(false);
        setPreferredSize(new Dimension(0, 56));

        JLabel label = new JLabel(title);
        label.setFont(Theme.bodyFont());
        label.setForeground(Theme.TEXT_PRIMARY);
        label.setBorder(BorderFactory.createEmptyBorder(0, Theme.COMPONENT_GAP, 0, Theme.COMPONENT_GAP));
        label.setToolTipText(title);
        add(label, BorderLayout.CENTER);
    }

    @Override
    protected void paintComponent(Graphics g) {
        Graphics2D g2 = (Graphics2D) g.create();
        g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);

        RoundRectangle2D.Float shape = new RoundRectangle2D.Float(0, 0, getWidth() - 1, getHeight() - 1,
                Theme.CORNER_ARC, Theme.CORNER_ARC);
        g2.setColor(Theme.CARD_BACKGROUND);
        g2.fill(shape);
        g2.setColor(Theme.BORDER);
        g2.draw(shape);

        g2.dispose();
        super.paintComponent(g);
    }
}
