package com.musicmanager.view;

import java.awt.Color;
import java.awt.Cursor;
import java.awt.Font;
import java.awt.Graphics;
import java.awt.Graphics2D;
import java.awt.RenderingHints;
import java.awt.event.MouseAdapter;
import java.awt.event.MouseEvent;
import java.awt.geom.RoundRectangle2D;

import javax.swing.BorderFactory;
import javax.swing.JButton;
import javax.swing.plaf.basic.BasicButtonUI;

/**
 * a flat, rounded, non-gradient JButton. Swing's default (Metal) button UI
 * paints a gradient bevel regardless of background color, so this swaps in
 * BasicButtonUI and paints its own rounded background, with a simple hover
 * shade and a distinct flat fill when disabled.
 */
public class FlatButton extends JButton {

    public FlatButton(String text, Color background, Color hoverBackground) {
        super(text);

        setUI(new BasicButtonUI());
        setForeground(Color.WHITE);
        setBackground(background);
        setFont(new Font(Theme.FONT_FAMILY, Font.BOLD, Theme.BUTTON_SIZE));
        setFocusPainted(false);
        setBorderPainted(false);
        setContentAreaFilled(false);
        setOpaque(false);
        setCursor(Cursor.getPredefinedCursor(Cursor.HAND_CURSOR));
        setBorder(BorderFactory.createEmptyBorder(Theme.BUTTON_PADDING_V, Theme.BUTTON_PADDING_H,
                Theme.BUTTON_PADDING_V, Theme.BUTTON_PADDING_H));

        addMouseListener(new MouseAdapter() {
            @Override
            public void mouseEntered(MouseEvent e) {
                if (isEnabled()) {
                    setBackground(hoverBackground);
                }
            }

            @Override
            public void mouseExited(MouseEvent e) {
                setBackground(background);
            }
        });
    }

    @Override
    protected void paintComponent(Graphics g) {
        Graphics2D g2 = (Graphics2D) g.create();
        g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g2.setColor(isEnabled() ? getBackground() : Theme.DISABLED);
        g2.fill(new RoundRectangle2D.Float(0, 0, getWidth(), getHeight(), Theme.CORNER_ARC, Theme.CORNER_ARC));
        g2.dispose();
        super.paintComponent(g);
    }

    /** a filled, accent colored button for primary actions */
    public static FlatButton primary(String text) {
        return new FlatButton(text, Theme.PRIMARY, Theme.PRIMARY_HOVER);
    }

    /** a filled, neutral colored button for secondary actions */
    public static FlatButton secondary(String text) {
        return new FlatButton(text, Theme.SECONDARY, Theme.SECONDARY_HOVER);
    }
}
