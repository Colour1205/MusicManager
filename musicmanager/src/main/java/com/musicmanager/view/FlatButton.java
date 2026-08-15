package com.musicmanager.view;

import java.awt.Color;
import java.awt.Cursor;
import java.awt.Font;
import java.awt.event.MouseAdapter;
import java.awt.event.MouseEvent;

import javax.swing.BorderFactory;
import javax.swing.JButton;
import javax.swing.plaf.basic.BasicButtonUI;

/**
 * a flat, non-gradient JButton. Swing's default (Metal) button UI paints a
 * gradient bevel regardless of background color, so this swaps in
 * BasicButtonUI, which just fills the background flatly, and adds a simple
 * hover shade.
 */
public class FlatButton extends JButton {

    private static final Color PRIMARY_BG = new Color(0x2D7DD2);
    private static final Color PRIMARY_BG_HOVER = new Color(0x2568AC);
    private static final Color SECONDARY_BG = new Color(0x546E7A);
    private static final Color SECONDARY_BG_HOVER = new Color(0x44575F);
    private static final Color TEXT_COLOR = Color.WHITE;

    public FlatButton(String text, Color background, Color hoverBackground) {
        super(text);

        setUI(new BasicButtonUI());
        setForeground(TEXT_COLOR);
        setBackground(background);
        setFont(getFont().deriveFont(Font.BOLD, 13f));
        setFocusPainted(false);
        setBorderPainted(false);
        setContentAreaFilled(true);
        setOpaque(true);
        setCursor(Cursor.getPredefinedCursor(Cursor.HAND_CURSOR));
        setBorder(BorderFactory.createEmptyBorder(10, 24, 10, 24));

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

    /** a filled, accent colored button for primary actions */
    public static FlatButton primary(String text) {
        return new FlatButton(text, PRIMARY_BG, PRIMARY_BG_HOVER);
    }

    /** a filled, neutral colored button for secondary actions */
    public static FlatButton secondary(String text) {
        return new FlatButton(text, SECONDARY_BG, SECONDARY_BG_HOVER);
    }
}
